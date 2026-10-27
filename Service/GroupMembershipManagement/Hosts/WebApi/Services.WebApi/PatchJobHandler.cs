// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.AspNetCore.JsonPatch;
using Models;
using Models.ServiceBus;
using Models.SyncJobChange;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
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
using Microsoft.ApplicationInsights;
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
        private readonly TelemetryClient _telemetryClient;

        // Retain compatibility with old payloads without allowing non-UI fields to affect updates or approval routing.
        private static readonly HashSet<string> IgnoredPatchPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/RunId",
            "/Requestor",
            "/AllowEmptyDestination",
            "/LastRunTime",
            "/LastSuccessfulRunTime",
            "/LastSuccessfulStartTime",
            "/IsDryRunEnabled",
            "/DryRunTimeStamp",
            "/ChangeReason",
            "/BusinessJustification"
        };

        // Only these configuration fields may bypass human review; /Query is classified separately.
        private static readonly HashSet<string> AutoApprovableConfigurationPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/Period",
            "/StartDate",
            "/ScheduledDate",
            "/IgnoreThresholdOnce",
            "/ThresholdPercentageForAdditions",
            "/ThresholdPercentageForRemovals",
            "/Titles",
            "/Status",
            "/LastModifiedOnBehalfOfDisplayName",
            "/LastModifiedOnBehalfOfObjectId"
        };

        public PatchJobHandler(
            ILogger<PatchJobHandler> logger,
            IGraphGroupRepository graphGroupRepository,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            IDatabaseTitlesRepository titlesRepository,
            IDatabaseSettingsRepository databaseSettingsRepository,
            INotificationService notificationService,
            TelemetryClient telemetryClient,
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
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
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

            if (syncJob.Status == SyncStatus.DeveloperPaused.ToString() && !request.IsJobTenantWriter)
            {
                response.StatusCode = HttpStatusCode.Forbidden;
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

            if (request.PatchDocument.Operations.Any(operation =>
                IsDestinationPath(operation.path)
                || (operation.from != null && IsDestinationPath(operation.from))))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorCode = "DestinationCannotBeChanged";
                return response;
            }

            if (syncJob.Status == SyncStatus.InProgress.ToString() || syncJob.Status == SyncStatus.StuckInProgress.ToString())
            {
                response.StatusCode = HttpStatusCode.PreconditionFailed;
                response.ErrorCode = "JobInProgress";
                return response;
            }

            request.PatchDocument.Operations.RemoveAll(operation =>
            {
                if (string.IsNullOrEmpty(operation.path)) return false;
                var nestedPathIndex = operation.path.IndexOf('/', 1);
                var rootPath = nestedPathIndex < 0 ? operation.path : operation.path[..nestedPathIndex];
                return IgnoredPatchPaths.Contains(rootPath);
            });

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

                // The submission under review is what distinguishes an onboarding from an edit to an
                // existing job; the patch itself carries only SubmissionApproved/SubmissionRejected and
                // so cannot tell them apart. Resolved once here so every review outcome below reports
                // the same request type.
                var reviewRequestType = JobReviewedTelemetryHelper.ToRequestType(submission?.ChangeReason);

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

                        // The ownership precheck rejected this regardless of what the reviewer chose, so it
                        // is attributed to the system rather than to the reviewer who opened it. Emitted
                        // after the rejection and its audit row are persisted.
                        JobReviewedTelemetryHelper.TrackSystemRejection(
                            _telemetryClient,
                            request.SyncJobId,
                            (Guid)groupId,
                            syncJob.MembershipType,
                            reviewRequestType);

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
                    // ValidateAndUpdateSyncJob returned null, so the job update and its SubmissionRejected
                    // audit row are both persisted. Emitted before the notification send so that a
                    // notification failure cannot suppress a review that already took effect.
                    JobReviewedTelemetryHelper.TrackManualRejection(
                        _telemetryClient,
                        request.SyncJobId,
                        (Guid)groupId,
                        syncJob.MembershipType,
                        reviewRequestType,
                        request.UserIdentity);

                    await _notificationService.SendSubmissionRejectedNotificationAsync(syncJob, syncJobChange);
                }
                else if (newStatus == SyncStatus.Idle.ToString())
                {
                    // ValidateAndUpdateSyncJob returned null, so the job update and its SubmissionApproved
                    // audit row are both persisted. Emitted before the notification send so that a
                    // notification failure cannot suppress a review that already took effect.
                    JobReviewedTelemetryHelper.TrackManualApproval(
                        _telemetryClient,
                        request.SyncJobId,
                        (Guid)groupId,
                        syncJob.MembershipType,
                        reviewRequestType,
                        request.UserIdentity);

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
                // Configuration updates must not resume paused, rejected, or errored jobs.
                var preEditStatus = syncJob.Status;

                if (_autoApproverQueueRepository == null)
                {
                    // The AutoApprover host is disabled: preserve the legacy behavior for every update.
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

                    // Classify only effective operations; ignored fields have already been removed.
                    // Status and on-behalf attribution still fall back to human review.
                    var touchesOnlyAllowlistedNonQueryFields = request.PatchDocument.Operations
                        .Where(op => !string.Equals(op.path, "/Query", StringComparison.OrdinalIgnoreCase))
                        .All(op => AutoApprovableConfigurationPaths.Contains(op.path ?? string.Empty));

                    var isConfigurationOnly = !isQueryChanged && touchesOnlyAllowlistedNonQueryFields;

                    var canAutoApproveConfiguration = preEditStatus is nameof(SyncStatus.Idle)
                        or nameof(SyncStatus.ThresholdExceeded);
                    var preserveConfigurationStatus = preEditStatus is nameof(SyncStatus.Error)
                        or nameof(SyncStatus.MembershipDataNotFound)
                        or nameof(SyncStatus.DestinationGroupNotFound)
                        or nameof(SyncStatus.NotOwnerOfDestinationGroup)
                        or nameof(SyncStatus.SourceGroupNotFound)
                        or nameof(SyncStatus.FilePathNotValid)
                        or nameof(SyncStatus.QueryNotValid)
                        or nameof(SyncStatus.FileNotFound)
                        or nameof(SyncStatus.CustomerPaused)
                        or nameof(SyncStatus.ErroredDueToStuckInProgress)
                        or nameof(SyncStatus.TeamsChannelNotDestination)
                        or nameof(SyncStatus.DestinationQueryNotValid)
                        or nameof(SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup)
                        or nameof(SyncStatus.StandardTeamsChannel)
                        or nameof(SyncStatus.TeamsChannelError)
                        or nameof(SyncStatus.SchemaError)
                        or nameof(SyncStatus.TransientError)
                        or nameof(SyncStatus.DeveloperPaused)
                        or nameof(SyncStatus.SubmissionRejected)
                        or nameof(SyncStatus.NestedGroupsFound)
                        or nameof(SyncStatus.ManagerNotFound);

                    if (isConfigurationOnly && preserveConfigurationStatus)
                    {
                        // Saving configuration is not an approval or a recovery from an error/paused/rejected state.
                        var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, preEditStatus);
                        if (result != null) return result;
                        _logger.ConfigurationUpdateStatusPreserved(request.SyncJobId, preEditStatus);
                    }
                    else if (isConfigurationOnly && !canAutoApproveConfiguration)
                    {
                        var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, SyncStatus.PendingReview.ToString());
                        if (result != null) return result;
                    }
                    else if (!touchesOnlyAllowlistedNonQueryFields)
                    {
                        // Any non-allowlisted field requires human review, even when /Query also changed. The
                        // AutoApprover evaluates only the query and must not implicitly approve unrelated changes.
                        var result = await ValidateAndUpdateSyncJob(request, syncJob, syncJobChange, SyncStatus.PendingReview.ToString());
                        if (result != null) return result;
                    }
                    else
                    {
                        // All actual auto-approvals are recorded by the AutoApprover host.
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
                            Origin = isConfigurationOnly ? AutoApprovalOrigin.ConfigUpdate : AutoApprovalOrigin.Update,
                            ConfigurationQuery = isConfigurationOnly ? syncJob.Query : null
                        };

                        var autoApprovalBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(autoApprovalMessage));
                        var autoApprovalServiceBusMessage = new ServiceBusMessage
                        {
                            // Duplicate detection is enabled on the queue. Each update submission needs its own ID so
                            // it cannot collide with onboarding or another edit for the same job.
                            MessageId = $"{request.SyncJobId}_{Guid.NewGuid()}",
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

            var appliesPatchDocument =
                request.ChangeReason == SyncJobChangeReason.Update.ToString()
                || request.ChangeReason == SyncJobChangeReason.ScheduledNow.ToString();
            if (appliesPatchDocument)
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

            var updatesQuery = appliesPatchDocument
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

            if (updatesQuery)
            {
                var groupIdentitySelectionError =
                    GroupIdentitySelectionValidator.Validate(updatedSyncJob.Query);
                if (groupIdentitySelectionError != null)
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = groupIdentitySelectionError.ErrorCode;
                    return response;
                }

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

        private static bool IsDestinationPath(string? path)
        {
            var property = path?.Split('/').ElementAtOrDefault(1);
            return string.IsNullOrEmpty(property)
                || string.Equals(property, nameof(SyncJobPatch.Destination), StringComparison.OrdinalIgnoreCase)
                || string.Equals(property, nameof(SyncJobPatch.TargetOfficeGroupId), StringComparison.OrdinalIgnoreCase);
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

        private SyncJobPatch MapEntityToDto(Guid syncJobId, SyncJob syncJob)
        {
            return new SyncJobPatch
            {
                Status = syncJob.Status,
                Period = syncJob.Period,
                Query = syncJob.Query,
                StartDate = syncJob.StartDate,
                IgnoreThresholdOnce = syncJob.IgnoreThresholdOnce,
                ThresholdPercentageForAdditions = syncJob.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = syncJob.ThresholdPercentageForRemovals,
                ScheduledDate = syncJob.ScheduledDate,
            };
        }

        private SyncJob MapDtoToEntity(SyncJob syncJob, SyncJobPatch syncJobPatch, Guid syncJobId)
        {
            syncJob.Id = syncJobId;
            syncJob.Status = syncJobPatch.Status;
            syncJob.Period = syncJobPatch.Period;
            syncJob.Query = syncJobPatch.Query;
            syncJob.StartDate = syncJobPatch.StartDate;
            syncJob.IgnoreThresholdOnce = syncJobPatch.IgnoreThresholdOnce;
            syncJob.ThresholdPercentageForAdditions = syncJobPatch.ThresholdPercentageForAdditions;
            syncJob.ThresholdPercentageForRemovals = syncJobPatch.ThresholdPercentageForRemovals;
            syncJob.ScheduledDate = syncJobPatch.ScheduledDate;

            return syncJob;
        }
    }
}
