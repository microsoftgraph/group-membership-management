// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using Services.WebApi.Validators;
using System.Net;
using WebApi.Models.DTOs;
using SyncJob = Models.SyncJob;

namespace Services.WebApi
{
    public class PatchJobHandler : RequestHandlerBase<PatchJobRequest, PatchJobResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;

        public PatchJobHandler(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            IDatabaseSettingsRepository databaseSettingsRepository)
            : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
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

            if (string.IsNullOrWhiteSpace(request.ChangeReason))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorCode = "ChangeReasonIsRequired";
                return response;
            }

            var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, syncJob.TargetOfficeGroupId);
            if (!(isGroupOwner || request.IsAllowed))
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

            var status = request.PatchDocument.Operations.FirstOrDefault(op => op.path == "/Status")?.value?.ToString();
            if (status == null)
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorCode = "StatusIsRequired";
                return response;
            }

            var changedOnBehalfOfDisplayName = request.PatchDocument.Operations.FirstOrDefault(op => op.path == "/LastModifiedOnBehalfOfDisplayName")?.value?.ToString();

            var syncJobChange = new SyncJobChange
            {
                SyncJobId = request.SyncJobId,
                ChangeTime = DateTime.UtcNow,
                ChangedByObjectId = Guid.Parse(request.UserIdentity),
                ChangedByDisplayName = request.UserDisplayName,
                ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName,
                ChangeSource = SyncJobChangeSource.WebApp,
                BusinessJustification = request.BusinessJustification
            };

            if (!string.IsNullOrEmpty(changedOnBehalfOfDisplayName))
            {
                syncJobChange.ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName;
                syncJob.Requestor = changedOnBehalfOfDisplayName;
            }

            var syncJobToPatch = MapEntityToDto(request.SyncJobId, syncJob);

            // Enabling or disabling a job status update change
            if (request.ChangeReason == SyncJobChangeReason.StatusUpdate.ToString())
            {
                var result = await ValidateAndUpdateSyncJob(request, syncJobToPatch, syncJob, syncJobChange, request.ChangeReason, status);
                if (result != null) return result;
            }

            // Approving or rejecting a submission
            if (request.ChangeReason == SyncJobChangeReason.SubmissionApproved.ToString() || request.ChangeReason == SyncJobChangeReason.SubmissionRejected.ToString())
            {
                var canReviewOwnSubmissions = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.CanReviewOwnSubmissions);
                var canReviewOwnSubmissionsValue = canReviewOwnSubmissions != null ? bool.Parse(canReviewOwnSubmissions.SettingValue) : true;
                var requestorUserId = Guid.Parse(request.UserIdentity);

                if (canReviewOwnSubmissionsValue == false && (requestorUserId == syncJobChange.ChangedByObjectId))
                {
                    response.StatusCode = HttpStatusCode.Forbidden;
                    return response;
                }

                var result = await ValidateAndUpdateSyncJob(request, syncJobToPatch, syncJob, syncJobChange, request.ChangeReason, status);
                if (result != null) return result;
            }

            // Updating a job
            if (request.ChangeReason == SyncJobChangeReason.Update.ToString())
            {
                var result = await ValidateAndUpdateSyncJob(request, syncJobToPatch, syncJob, syncJobChange, SyncJobChangeReason.Update.ToString(), SyncStatus.PendingReview.ToString());
                if (result != null) return result;
            }

            return response;
        }

        private async Task<PatchJobResponse?> ValidateAndUpdateSyncJob(PatchJobRequest request, SyncJobPatch syncJobToPatch, SyncJob syncJob, SyncJobChange syncJobChange, string changeReason, string status)
        {
            var response = new PatchJobResponse();

            request.PatchDocument.ApplyTo(syncJobToPatch);

            var validationResult = Validate(syncJobToPatch);
            if (!validationResult.IsValid)
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorCode = validationResult.ErrorCode;
                return response;
            }

            var updatedSyncJob = MapDtoToEntity(syncJob, syncJobToPatch, request.SyncJobId);
            updatedSyncJob.Status = status;
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
