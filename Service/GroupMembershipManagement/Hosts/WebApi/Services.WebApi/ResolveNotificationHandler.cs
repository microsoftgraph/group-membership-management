// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Models;
using Models.SyncJobChange;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Services.Contracts;
using Services.Contracts.Notifications;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class ResolveNotificationHandler : RequestHandlerBase<ResolveNotificationRequest, ResolveNotificationResponse>
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IThresholdNotificationService _thresholdNotificationService;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGMMEmailReceivers _gmmEmailReceivers;

        public ResolveNotificationHandler(ILoggingRepository loggingRepository,
                              INotificationRepository notificationRepository,
                              IDatabaseSyncJobsRepository syncJobRepository,
                              ISyncJobChangeRepository syncJobChangeRepository,
                              IGraphGroupRepository graphGroupRepository,
                              TelemetryClient telemetryClient,
                              IThresholdNotificationService thresholdNotificationService,
                              IGMMEmailReceivers gmmEmailReceivers) : base(loggingRepository)
        {
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _thresholdNotificationService = thresholdNotificationService ?? throw new ArgumentNullException(nameof(thresholdNotificationService));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _gmmEmailReceivers = gmmEmailReceivers ?? throw new ArgumentNullException(nameof(gmmEmailReceivers));
        }

        protected override async Task<ResolveNotificationResponse> ExecuteCoreAsync(ResolveNotificationRequest request)
        {
            var response = new ResolveNotificationResponse();
            var thresholdNotification = await _notificationRepository.GetThresholdNotificationByIdAsync(request.ThresholdNotificationId);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"ResolveNotificationHandler request: " +
                $"ThresholdNotificationId: {request.ThresholdNotificationId}, TargetOfficeGroupId: {thresholdNotification?.TargetOfficeGroupId}"
            });
            if (thresholdNotification == null)
            {
                response.CardJson = _thresholdNotificationService.CreateNotFoundNotificationCard(request.ThresholdNotificationId);
                return response;
            }

            var isInAuthorizedGroup = false;

            var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentifier, thresholdNotification.TargetOfficeGroupId);
            if (!isGroupOwner)
            {
                // Check if user is in the list of GMM Support / Actionable Message Viewer Group
                isInAuthorizedGroup = await _graphGroupRepository.IsEmailRecipientMemberOfGroupAsync(request.UserIdentifier, _gmmEmailReceivers.ActionableMessageViewerGroupId);

                if (!isInAuthorizedGroup)
                {
                    // Unauthorized
                    response.CardJson = await _thresholdNotificationService.CreateUnauthorizedNotificationCardAsync(thresholdNotification);
                    return response;
                }
            }

            if (thresholdNotification.Status != ThresholdNotificationStatus.Resolved)
            {
                var resolvedByValue = request.UserIdentifier;
                Guid userId;

                if (isInAuthorizedGroup)
                {
                    try
                    {
                        var groupName = await _graphGroupRepository.GetGroupNameAsync(_gmmEmailReceivers.ActionableMessageViewerGroupId);
                        resolvedByValue = groupName;
                    }
                    catch(Exception e)
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"Error getting group name: {e.Message}"
                        });
                        resolvedByValue = "GMM Support";
                    }
                }
                else if (Guid.TryParse(resolvedByValue, out userId))
                {
                    var user = await _graphGroupRepository.GetUserByUpnOrIdAsync(userId.ToString(), true);
                    resolvedByValue = user != null ?  user.Mail : request.UserIdentifier;
                }

                var resolution = Enum.Parse<ThresholdNotificationResolution>(request.Resolution);
                thresholdNotification.Status = ThresholdNotificationStatus.Resolved;
                thresholdNotification.CardState = ThresholdNotificationCardState.NoCard;
                thresholdNotification.Resolution = resolution;
                thresholdNotification.ResolvedBy = resolvedByValue;
                thresholdNotification.ResolvedTime = DateTime.UtcNow;

                await handleSyncJobResolution(thresholdNotification);
                await _notificationRepository.SaveNotificationAsync(thresholdNotification);
            }
            var timeElapsedForResponse = ((thresholdNotification.ResolvedTime - thresholdNotification.CreatedTime).TotalSeconds).ToString();
            TrackNotificationResponseEvent(thresholdNotification.Id, timeElapsedForResponse);

            response.CardJson = await _thresholdNotificationService.CreateResolvedNotificationCardAsync(thresholdNotification);
            return response;
        }

        private async Task handleSyncJobResolution(ThresholdNotification notification)
        {
            var changeReason = string.Empty;
            var job = await _syncJobRepository.GetSyncJobAsync(notification.SyncJobId);

            if (notification.Resolution == ThresholdNotificationResolution.IgnoreOnce)
            {
                job.IgnoreThresholdOnce = true;
                job.Status = SyncStatus.Idle.ToString();
                changeReason = SyncJobChangeReason.IgnoreThresholdOnce.ToString();
                await _syncJobRepository.UpdateSyncJobFromNotificationAsync(job, SyncStatus.Idle);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resolved Notification. Setting the status of the sync back to Idle." });
            }
            else if (notification.Resolution == ThresholdNotificationResolution.Paused)
            {
                job.Status = SyncStatus.CustomerPaused.ToString();
                changeReason = SyncJobChangeReason.StatusUpdate.ToString();
                await _syncJobRepository.UpdateSyncJobFromNotificationAsync(job, SyncStatus.CustomerPaused);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resolved Notification. Setting the status of the sync to CustomerPaused." });
            }

            var syncJobChange = new SyncJobChange
            {
                SyncJobId = notification.SyncJobId,
                ChangeTime = notification.ResolvedTime,
                ChangedByDisplayName = notification.ResolvedBy,
                ChangeSource = SyncJobChangeSource.Email,
                ChangeReason = changeReason,
                ChangeDetails = SyncJobSerializationHelper.SerializeSyncJob(job)
            };

            await _syncJobChangeRepository.Save(syncJobChange);
        }
        private void TrackNotificationResponseEvent(Guid groupId, string timeElapsedForResponse)
        {
            var notificationResponseEvent = new Dictionary<string, string>
            {
                { "TargetGroupId", groupId.ToString() },
                { "ResponseTimeSeconds", timeElapsedForResponse }
            };
            _telemetryClient.TrackEvent("NotificationResponseReceived", notificationResponseEvent);
        }
    }
}