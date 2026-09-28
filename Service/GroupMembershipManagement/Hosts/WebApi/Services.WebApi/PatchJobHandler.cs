// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.AspNetCore.JsonPatch;
using Models;
using Models.ServiceBus;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.Contracts;
using Services.Helpers;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using Services.WebApi.Validators;
using System.Net;
using System.Text;
using System.Text.Json;
using WebApi.Models.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SyncJob = Models.SyncJob;
using SyncJobChange = Models.SyncJobChange.SyncJobChange;

namespace Services.WebApi
{
    public class PatchJobHandler : RequestHandlerBase<PatchJobRequest, PatchJobResponse>
    {
        private readonly ILogger<PatchJobHandler> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IDatabaseTitlesRepository _titlesRepository;
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        private readonly INotificationService _notificationService;
        private readonly IServiceBusQueueRepository? _autoApproverQueueRepository;

        // Explicit allowlist of low-risk configuration fields that may be auto-approved on the config-only fast path.
        // Everything NOT listed here (notably /Destination and /TargetOfficeGroupId, which redirect the sync to a
        // different group, plus /Status, /RunId, /Requestor, /AllowEmptyDestination and the Last*/OnBehalfOf fields)
        // must go through human review: ownership is authorized against the ORIGINAL group only (never the post-patch
        // destination), so a non-allowlisted change must never be silently auto-approved. /Query is intentionally
        // absent — it is classified separately via a dry-run query comparison.
        private static readonly HashSet<string> AutoApprovableConfigurationPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/Period",
            "/StartDate",
            "/ScheduledDate",
            "/IgnoreThresholdOnce",
            "/ThresholdPercentageForAdditions",
            "/ThresholdPercentageForRemovals",
            "/IsDryRunEnabled",
            "/DryRunTimeStamp",
            "/Titles"
        };

        public PatchJobHandler(
            ILogger<PatchJobHandler> logger,
            IGraphGroupRepository graphGroupRepository,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            IDatabaseTitlesRepository titlesRepository,
            IDatabaseSettingsRepository databaseSettingsRepository,
            INotificationService notificationService,
            [FromKeyedServices("AutoApprover")] IServiceBusQueueRepository? autoApproverQueueRepository = null)
            : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _titlesRepository = titlesRepository ?? throw new ArgumentNullException(nameof(titlesRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _autoApproverQueueRepository = autoApproverQueueRepository;
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
            
            // Only Job Tenant Writers and Submission Reviewers may record an "on behalf of" owner; ignore carried-over values for anyone else.
            string? changedOnBehalfOfDisplayName = null;
            string? changedOnBehalfOfObjectId = null;
            if (request.IsOnBehalfAllowed)
            {
                changedOnBehalfOfDisplayName = request.PatchDocument.Operations.FirstOrDefault(op => op.path == "/LastModifiedOnBehalfOfDisplayName")?.value?.ToString();
                changedOnBehalfOfObjectId = request.PatchDocument.Operations.FirstOrDefault(op => op.path == "/LastModifiedOnBehalfOfObjectId")?.value?.ToString();
            }

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
            // If the job is awaiting auto-approval, it cannot be updated until the AutoApprover resolves it
            else if (syncJob.Status == SyncStatus.PendingAutoApproval.ToString())
            {
                response.StatusCode = HttpStatusCode.PreconditionFailed;
                response.ErrorCode = "JobInPendingAutoApprovalStateCannotBeUpdated";
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

            // Handle ScheduleNow
            if (request.ChangeReason == SyncJobChangeReason.ScheduledNow.ToString())
            {
                var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, syncJob.Status);
                if (result != null) return result;
            }

            // Handle General Update
            if (request.ChangeReason == SyncJobChangeReason.Update.ToString())
            {
                // Capture the status the job had before this edit; the config-only fast path must preserve it.
                var preEditStatus = syncJob.Status;
                var isConfigUpdateAutoApprovalEnabled = await IsAutoApprovalForConfigurationUpdatesEnabledAsync();

                if (!isConfigUpdateAutoApprovalEnabled)
                {
                    // Feature off: preserve today's behavior byte-for-byte — every update goes to PendingReview.
                    var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, SyncStatus.PendingReview.ToString());
                    if (result != null) return result;
                }
                else
                {
                    // Classification must reflect the FINAL query the patch produces, not just the first /Query
                    // operation: JSON Patch allows multiple ops on the same path and ApplyTo applies them all in
                    // order, so reading only the first op could misclassify a real query change as config-only.
                    // When /Query is absent the query cannot change, so it is config-only regardless of the stored
                    // value (even if that stored value is unparseable legacy data).
                    var hasQueryOperation = request.PatchDocument.Operations
                        .Any(op => string.Equals(op.path, "/Query", StringComparison.OrdinalIgnoreCase));

                    bool isQueryChanged;
                    if (!hasQueryOperation)
                    {
                        isQueryChanged = false;
                    }
                    else
                    {
                        string? finalQuery = null;
                        try
                        {
                            // Dry-run the patch against a throwaway DTO to obtain the fully-applied query. This does
                            // not mutate syncJob; ValidateAndUpdateSyncJob re-applies the patch on the real path.
                            var probe = MapEntityToDto(request.SyncJobId, syncJob);
                            request.PatchDocument.ApplyTo(probe);
                            finalQuery = probe.Query;
                        }
                        catch
                        {
                            // A malformed patch fails closed to the query-changing path; ValidateAndUpdateSyncJob
                            // will surface the same failure when it re-applies the patch.
                            finalQuery = null;
                        }

                        // Normalized-equal ⇒ query unchanged. Any difference, or an unparseable query on either
                        // side, fails closed to the query-changing path.
                        isQueryChanged = finalQuery == null
                            || JsonParser.CompareQueries(syncJob.Query, finalQuery) != JsonParser.QueryEquivalence.Equal;
                    }

                    // A configuration-only submission may only touch the allowlist of low-risk fields. Any operation
                    // on a non-allowlisted field (e.g. /Destination or /TargetOfficeGroupId, which redirect the sync
                    // to a group the requester may not own — authorization above validated the ORIGINAL group only)
                    // must fall back to human review rather than the auto-approval fast path.
                    var touchesOnlyAllowlistedNonQueryFields = request.PatchDocument.Operations
                        .Where(op => !string.Equals(op.path, "/Query", StringComparison.OrdinalIgnoreCase))
                        .All(op => AutoApprovableConfigurationPaths.Contains(op.path ?? string.Empty));

                    var isConfigurationOnly = !isQueryChanged && touchesOnlyAllowlistedNonQueryFields;

                    // Status preservation only makes sense for operational jobs. A job that is not currently
                    // Idle or CustomerPaused (for example one a reviewer previously left in SubmissionRejected, or
                    // an errored job) must not be silently auto-approved back into a non-schedulable, non-reviewable
                    // state — that would strand it while telling the submitter it was approved. Such edits fall back
                    // to today's PendingReview behavior so a human re-evaluates them.
                    var canPreserveStatus = preEditStatus == SyncStatus.Idle.ToString()
                        || preEditStatus == SyncStatus.CustomerPaused.ToString();

                    if (isConfigurationOnly && canPreserveStatus)
                    {
                        // Config-only fast path: apply the change, preserve the pre-edit status (do NOT resume a paused
                        // job), audit with UpdateAutoApproved, and notify only the submitter. The handler forces no
                        // timing field, so nothing here expedites the sync (FR-010, FR-011).
                        var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, preEditStatus, SyncJobChangeReason.UpdateAutoApproved.ToString());
                        if (result != null) return result;

                        _logger.ConfigurationUpdateAutoApproved(request.SyncJobId);
                        await _notificationService.SendSubmissionApprovedNotificationAsync(syncJob, syncJobChange);
                    }
                    else if (isConfigurationOnly)
                    {
                        // Config-only edit to a non-operational job (e.g. SubmissionRejected/error state): route to
                        // PendingReview exactly as today rather than preserving a status that would strand the job.
                        var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, SyncStatus.PendingReview.ToString());
                        if (result != null) return result;
                    }
                    else if (!isQueryChanged)
                    {
                        // The query did not change, but the patch touches a non-allowlisted field (e.g. /Destination
                        // or /TargetOfficeGroupId). Such a change can redirect the sync to a different group while
                        // authorization above only validated the original group, so it must go through human review
                        // rather than be auto-approved.
                        var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, SyncStatus.PendingReview.ToString());
                        if (result != null) return result;
                    }
                    else if (_autoApproverQueueRepository == null)
                    {
                        // Query changed but the AutoApprover host is disabled: fall back to human review; never leave
                        // the job in PendingAutoApproval, which nothing would resolve (FR-014).
                        var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, SyncStatus.PendingReview.ToString());
                        if (result != null) return result;
                    }
                    else
                    {
                        // Query-changing path: route the newly submitted query through the same AutoApprover the
                        // onboarding producer uses (mirrors PostJobHandler).
                        var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, SyncStatus.PendingAutoApproval.ToString());
                        if (result != null) return result;

                        var autoApprovalMessage = new AutoApprovalQueueMessage
                        {
                            SyncJobId = request.SyncJobId,
                            RequestorObjectId = request.UserIdentity,
                            RequestorDisplayName = request.UserDisplayName,
                            ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName,
                            ChangedOnBehalfOfObjectId = changedOnBehalfOfObjectId,
                            BusinessJustification = request.BusinessJustification,
                            Origin = AutoApprovalOrigin.Update
                        };

                        var autoApprovalBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(autoApprovalMessage));
                        var autoApprovalServiceBusMessage = new ServiceBusMessage
                        {
                            MessageId = request.SyncJobId.ToString(),
                            Body = autoApprovalBody
                        };

                        try
                        {
                            await _autoApproverQueueRepository.SendMessageAsync(autoApprovalServiceBusMessage);
                            _logger.AutoApproverMessageSent(autoApprovalServiceBusMessage.MessageId);
                        }
                        catch (Exception ex)
                        {
                            // ValidateAndUpdateSyncJob mutated `syncJob` in place (MapDtoToEntity returns the same
                            // instance) so it already carries the submitted query with PendingAutoApproval. The
                            // AutoApprover only acts on queue messages; if enqueueing failed nothing would move the
                            // job out of PendingAutoApproval, so revert it to PendingReview for human review while
                            // keeping the submitted query intact (FR-013).
                            _logger.AutoApproverMessageSendFailed(request.SyncJobId, ex);

                            try
                            {
                                syncJob.Status = SyncStatus.PendingReview.ToString();
                                await _databaseSyncJobsRepository.UpdateSyncJobsAsync(new[] { syncJob });
                            }
                            catch (Exception revertEx)
                            {
                                _logger.AutoApproverRevertToPendingReviewFailed(request.SyncJobId, revertEx);
                            }
                        }
                    }
                }
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
        private async Task<PatchJobResponse> ValidateAndUpdateSyncJob(PatchJobRequest request, SyncJob syncJob, SyncJobChange syncJobChange, string status, string? changeReasonOverride = null)
        {
            var syncJobToPatch = MapEntityToDto(request.SyncJobId, syncJob);
            var changeReason = changeReasonOverride ?? request.ChangeReason;

            var response = new PatchJobResponse();

            if(request.ChangeReason == SyncJobChangeReason.Update.ToString() || request.ChangeReason == SyncJobChangeReason.ScheduledNow.ToString())
            {
                try
                {
                    request.PatchDocument.ApplyTo(syncJobToPatch);
                }
                catch (Exception ex)
                {
                    _logger.PatchDocumentApplyFailed(request.SyncJobId, ex);
                    throw;
                }
            }
            syncJobToPatch.Status = status;

            var updatesQuery = request.ChangeReason == SyncJobChangeReason.Update.ToString()
                && request.PatchDocument.Operations.Any(operation =>
                    string.Equals(operation.path, "/Query", StringComparison.OrdinalIgnoreCase));
            if (updatesQuery && IsEmptyQuery(syncJobToPatch.Query))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorCode = "EmptyQueryIsNotAllowed";
                return response;
            }

            var validationResult = Validate(syncJobToPatch);
            if (!validationResult.IsValid)
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorCode = validationResult.ErrorCode;
                return response;
            }

            var updatedSyncJob = MapDtoToEntity(syncJob, syncJobToPatch, request.SyncJobId);

            // Re-run the US3 TeamsChannel source validation whenever the query is edited, so an update
            // cannot persist a TeamsChannel source that the create path (PostJobHandler) would reject.
            if (updatesQuery)
            {
                var teamsChannelSourceError = TeamsChannelSourcePartValidator.Validate(updatedSyncJob);
                if (teamsChannelSourceError != null)
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = teamsChannelSourceError.ErrorCode;
                    return response;
                }
            }

            await _databaseSyncJobsRepository.UpdateSyncJobsAsync(new[] { updatedSyncJob });

            syncJobChange.ChangeDetails = SyncJobSerializationHelper.SerializeSyncJob(updatedSyncJob);
            syncJobChange.ChangeReason = changeReason;

            await _syncJobChangeRepository.Save(syncJobChange);
            return null;
        }

        private static bool IsEmptyQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            try
            {
                using var document = JsonDocument.Parse(query);
                return document.RootElement.ValueKind == JsonValueKind.Null
                    || document.RootElement.ValueKind == JsonValueKind.Array
                    && document.RootElement.GetArrayLength() == 0;
            }
            catch (JsonException)
            {
                return false;
            }
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
                _logger.AITitleSettingRetrievalFailed(ex);
                return false;
            }
        }

        private async Task<bool> IsAutoApprovalForConfigurationUpdatesEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForConfigurationUpdatesEnabled);
                return setting != null && bool.TryParse(setting.SettingValue, out bool result) && result;
            }
            catch (Exception ex)
            {
                _logger.AutoApprovalForConfigurationUpdatesSettingRetrievalFailed(ex);
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
                ScheduledDate = syncJob.ScheduledDate,
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
            syncJob.ScheduledDate = syncJobPatch.ScheduledDate;

            return syncJob;
        }
    }
}
