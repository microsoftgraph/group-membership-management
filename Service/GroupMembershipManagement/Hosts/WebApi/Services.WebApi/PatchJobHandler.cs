// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.JsonPatch;
using Models;
using Models.SyncJobChange;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using Services.WebApi.Validators;
using System.Net;
using System.Text.Json;
using WebApi.Models.DTOs;
using LogMessage = Models.LogMessage;
using SyncJob = Models.SyncJob;
using SyncJobChange = Models.SyncJobChange.SyncJobChange;

namespace Services.WebApi
{
    public class PatchJobHandler : RequestHandlerBase<PatchJobRequest, PatchJobResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IDatabaseTitlesRepository _titlesRepository;
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        private readonly INotificationService _notificationService;
        private readonly IThresholdConfig _thresholdConfig;

        public PatchJobHandler(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            IDatabaseTitlesRepository titlesRepository,
            IDatabaseSettingsRepository databaseSettingsRepository,
            INotificationService notificationService,
            IThresholdConfig thresholdConfig)
            : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _titlesRepository = titlesRepository ?? throw new ArgumentNullException(nameof(titlesRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
        }

        protected override async Task<PatchJobResponse> ExecuteCoreAsync(PatchJobRequest request)
        {
            var response = new PatchJobResponse();

            var syncJob = await _databaseSyncJobsRepository.GetSyncJobAsync(request.SyncJobId);
            if (syncJob == null)
            {
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            var groupId = syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString()
                ? syncJob.Channel?.GroupId
                : syncJob.Group?.GroupId;

            if (groupId == null)
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorCode = "GroupIdNotFound";
                return response;
            }

            var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, (Guid)groupId);
            if (!isGroupOwner && !request.IsAllowed)
            {
                response.StatusCode = HttpStatusCode.Forbidden;
                return response;
            }

            if (syncJob.Status == SyncStatus.InProgress.ToString() || syncJob.Status == SyncStatus.StuckInProgress.ToString())
            {
                response.StatusCode = HttpStatusCode.PreconditionFailed;
                response.ErrorCode = "JobInProgress";
                return response;
            }

            var isAITitleEnabled = await IsAITitleEnabledAsync();

            // Use TitlesValue if Titles operation was present, otherwise get from PatchDocument
            string titles = null;
            if (request.HasTitlesOperation)
            {
                titles = request.TitlesValue;
            }
            else
            {
                titles = request.PatchDocument.Operations.FirstOrDefault(op => op.path == "/Titles")?.value?.ToString();
            }
            
            var changedOnBehalfOfDisplayName = request.PatchDocument.Operations.FirstOrDefault(op => op.path == "/LastModifiedOnBehalfOfDisplayName")?.value?.ToString();
            var changedOnBehalfOfObjectId = request.PatchDocument.Operations.FirstOrDefault(op => op.path == "/LastModifiedOnBehalfOfObjectId")?.value?.ToString();

            var syncJobChange = new SyncJobChange
            {
                SyncJobId = request.SyncJobId,
                ChangeTime = DateTime.UtcNow,
                ChangedByObjectId = Guid.Parse(request.UserIdentity),
                ChangedByDisplayName = request.UserDisplayName,
                ChangeSource = SyncJobChangeSource.WebApp,
                BusinessJustification = request.BusinessJustification,
                ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName != null && changedOnBehalfOfDisplayName != request.UserDisplayName ? changedOnBehalfOfDisplayName : null,
                ChangedOnBehalfOfObjectId = !string.IsNullOrEmpty(changedOnBehalfOfObjectId) && changedOnBehalfOfObjectId != request.UserIdentity ? new Guid(changedOnBehalfOfObjectId) : (Guid?)null
            };

            // If the job is in the PendingConfiguration status, it cannot be updated
            if (syncJob.Status == SyncStatus.PendingConfiguration.ToString())
            {
                response.StatusCode = HttpStatusCode.PreconditionFailed;
                response.ErrorCode = "JobInPendingConfigurationStateCannotBeUpdated";
                return response;
            }
            // Handle Reject/Approve for PendingReview
            else if (request.ChangeReason == SyncJobChangeReason.SubmissionApproved.ToString() || request.ChangeReason == SyncJobChangeReason.SubmissionRejected.ToString())
            {
                if (request.ChangeReason == SyncJobChangeReason.SubmissionApproved.ToString() && !request.CanApproveJob)
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = "OnlySubmissionReviewerCanApproveSubmission";
                    return response;
                }

                var canReviewOwnSubmissions = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.CanReviewOwnSubmissions);
                var canReviewOwnSubmissionsValue = canReviewOwnSubmissions != null ? bool.Parse(canReviewOwnSubmissions.SettingValue) : true;
                var requestorUserId = Guid.Parse(request.UserIdentity);

                var submission = await _syncJobChangeRepository.GetLastSyncJobChangeBySyncJobIdAsync(request.SyncJobId);

                if (canReviewOwnSubmissionsValue == false && (requestorUserId == submission.ChangedByObjectId) && 
                    (submission.ChangeReason == SyncJobChangeReason.Onboarding.ToString() || (submission.ChangeReason == SyncJobChangeReason.Update.ToString()))
                )
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = "ReviewerCannotReviewOwnSubmission";
                    return response;
                }

                // Verify that the submitter is still an owner
                var destinationOwners = await _graphGroupRepository.GetDestinationOwnersAsync(new List<Guid>() { (Guid)groupId });
                var isSubmitterOwner = false;
                if (destinationOwners != null && submission.ChangedByObjectId.HasValue)
                {
                    isSubmitterOwner = destinationOwners.Values.Any(ownerList => ownerList.Contains(submission.ChangedByObjectId.Value));
                }

                if (submission.ChangedOnBehalfOfObjectId == null)
                {
                    // If the request was not made on behalf of someone, check if the submitter is an owner
                    if (!isSubmitterOwner)
                    {
                        response.StatusCode = HttpStatusCode.BadRequest;
                        response.ErrorCode = "SubmitterNotOwner";

                        syncJob.Status = SyncStatus.SubmissionRejected.ToString();
                        await _databaseSyncJobsRepository.UpdateSyncJobsAsync(new[] { syncJob });

                        syncJobChange.ChangeReason = SyncJobChangeReason.SubmissionRejected.ToString();
                        await _syncJobChangeRepository.Save(syncJobChange);

                        return response;
                    }
                }

                var newStatus = request.PatchDocument.Operations.FirstOrDefault(op => op.path == "/Status")?.value?.ToString();
                if (newStatus != SyncStatus.Idle.ToString() && newStatus != SyncStatus.SubmissionRejected.ToString())
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = "ValidUpdateStatusIsRequired";
                    return response;
                }

                // Set ThresholdViolations to N-1 when submission is approved, so notification is sent on next threshold hit
                if (newStatus == SyncStatus.Idle.ToString())
                {
                    syncJob.ThresholdViolations = _thresholdConfig.NumberOfThresholdViolationsToNotify - 1;
                }

                var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, newStatus);
                if (result != null) return result;

                if (newStatus == SyncStatus.SubmissionRejected.ToString())
                {
                    await _notificationService.SendSubmissionRejectedNotificationAsync(syncJob, syncJobChange);
                }
                else if (newStatus == SyncStatus.Idle.ToString())
                {
                    await _notificationService.SendSubmissionApprovedNotificationAsync(syncJob, syncJobChange);
                }
            }
            // If the job is in the PendingReview status, it cannot be updated
            else if (syncJob.Status == SyncStatus.PendingReview.ToString())
            {
                response.StatusCode = HttpStatusCode.PreconditionFailed;
                response.ErrorCode = "JobInPendingReviewStateCannotBeUpdated";
                return response;
            }

            // Handle Enable/Disable/Pause
            if (request.ChangeReason == SyncJobChangeReason.StatusUpdate.ToString())
            {
                var newStatus = request.PatchDocument.Operations.FirstOrDefault(op => op.path == "/Status")?.value?.ToString();
                if (newStatus != SyncStatus.Idle.ToString() && newStatus != SyncStatus.CustomerPaused.ToString())
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = "ValidUpdateStatusIsRequired";
                    return response;
                }

                var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, newStatus);
                if (result != null) return result;
            }

            // Handle General Update
            if (request.ChangeReason == SyncJobChangeReason.Update.ToString())
            {
                var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, SyncStatus.PendingReview.ToString());
                if (result != null) return result;
            }

            if (isAITitleEnabled && request.HasTitlesOperation)
            {
                if (!string.IsNullOrEmpty(titles))
                {
                    var titlesArray = JsonSerializer.Deserialize<List<Title>>(titles);
                    if (titlesArray != null && titlesArray.Any())
                    {
                        foreach (var title in titlesArray)
                        {
                            title.SyncJobId = request.SyncJobId;
                        }
                        await _titlesRepository.UpdateTitlesAsync(titlesArray, request.SyncJobId);
                    }
                }
                else
                {
                    await _titlesRepository.DeleteTitlesAsync(request.SyncJobId);
                }
            }

            return response;
        }
        private async Task<PatchJobResponse> ValidateAndUpdateSyncJob(PatchJobRequest request, SyncJob syncJob, SyncJobChange syncJobChange, string status)
        {
            var syncJobToPatch = MapEntityToDto(request.SyncJobId, syncJob);
            var changeReason = request.ChangeReason;

            var response = new PatchJobResponse();

            if(request.ChangeReason == SyncJobChangeReason.Update.ToString())
            {
                try
                {
                    request.PatchDocument.ApplyTo(syncJobToPatch);
                }
                catch (Exception ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Error applying patch document for SyncJobId {request.SyncJobId}: {ex.Message}",
                        StackTrace = ex.StackTrace
                    });
                    throw;
                }
            }
            syncJobToPatch.Status = status;

            var validationResult = Validate(syncJobToPatch);
            if (!validationResult.IsValid)
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorCode = validationResult.ErrorCode;
                return response;
            }

            var updatedSyncJob = MapDtoToEntity(syncJob, syncJobToPatch, request.SyncJobId);
            await _databaseSyncJobsRepository.UpdateSyncJobsAsync(new[] { updatedSyncJob });

            syncJobChange.ChangeDetails = SyncJobSerializationHelper.SerializeSyncJob(updatedSyncJob);
            syncJobChange.ChangeReason = changeReason;

            await _syncJobChangeRepository.Save(syncJobChange);
            return null;
        }

        private ValidationResponse Validate(SyncJobPatch syncJobPatch)
        {
            var validators = new List<IValidator<SyncJobPatch>>
            {
                new StatusValidator()
            };

            var isValid = true;
            var errors = new List<string>();

            foreach (var validator in validators)
            {
                var validationResult = validator.Validate(syncJobPatch);
                if (!validationResult.IsValid)
                {
                    isValid = false;
                    errors.Add(validationResult.ErrorCode ?? string.Empty);
                }
            }

            return new ValidationResponse
            {
                IsValid = isValid,
                ErrorCode = string.Join("\n", errors)
            };
        }
        private async Task<bool> IsAITitleEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAITitleEnabled);
                return setting != null && bool.TryParse(setting.SettingValue, out bool result) && result;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error retrieving AI title setting: {ex.Message}"
                });
                return false;
            }
        }

        private SyncJobPatch MapEntityToDto(Guid syncJobId, SyncJob syncJob)
        {
            return new SyncJobPatch
            {
                RunId = syncJob.RunId,
                Requestor = syncJob.Requestor,
                TargetOfficeGroupId = syncJob.TargetOfficeGroupId,
                Destination = syncJob.Destination,
                AllowEmptyDestination = syncJob.AllowEmptyDestination,
                Status = syncJob.Status,
                LastRunTime = syncJob.LastRunTime,
                LastSuccessfulRunTime = syncJob.LastSuccessfulRunTime,
                LastSuccessfulStartTime = syncJob.LastSuccessfulStartTime,
                Period = syncJob.Period,
                Query = syncJob.Query,
                StartDate = syncJob.StartDate,
                IgnoreThresholdOnce = syncJob.IgnoreThresholdOnce,
                ThresholdPercentageForAdditions = syncJob.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = syncJob.ThresholdPercentageForRemovals,
                IsDryRunEnabled = syncJob.IsDryRunEnabled,
                DryRunTimeStamp = syncJob.DryRunTimeStamp,
                ThresholdViolations = syncJob.ThresholdViolations,
            };
        }

        private SyncJob MapDtoToEntity(SyncJob syncJob, SyncJobPatch syncJobPatch, Guid syncJobId)
        {
            syncJob.Id = syncJobId;
            syncJob.RunId = syncJobPatch.RunId;
            syncJob.Requestor = syncJobPatch.Requestor;
            syncJob.TargetOfficeGroupId = syncJobPatch.TargetOfficeGroupId;
            syncJob.Destination = syncJobPatch.Destination;
            syncJob.AllowEmptyDestination = syncJobPatch.AllowEmptyDestination;
            syncJob.Status = syncJobPatch.Status;
            syncJob.LastRunTime = syncJobPatch.LastRunTime;
            syncJob.LastSuccessfulRunTime = syncJobPatch.LastSuccessfulRunTime;
            syncJob.LastSuccessfulStartTime = syncJobPatch.LastSuccessfulStartTime;
            syncJob.Period = syncJobPatch.Period;
            syncJob.Query = syncJobPatch.Query;
            syncJob.StartDate = syncJobPatch.StartDate;
            syncJob.IgnoreThresholdOnce = syncJobPatch.IgnoreThresholdOnce;
            syncJob.ThresholdPercentageForAdditions = syncJobPatch.ThresholdPercentageForAdditions;
            syncJob.ThresholdPercentageForRemovals = syncJobPatch.ThresholdPercentageForRemovals;
            syncJob.IsDryRunEnabled = syncJobPatch.IsDryRunEnabled;
            syncJob.DryRunTimeStamp = syncJobPatch.DryRunTimeStamp;
            syncJob.ThresholdViolations = syncJobPatch.ThresholdViolations;

            return syncJob;
        }
    }
}
