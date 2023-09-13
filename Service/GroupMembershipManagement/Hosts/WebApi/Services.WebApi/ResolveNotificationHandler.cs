// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Models;
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
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IThresholdNotificationService _thresholdNotificationService;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILoggingRepository _loggingRepository;

        public ResolveNotificationHandler(ILoggingRepository loggingRepository,
                              INotificationRepository notificationRepository,
                              IDatabaseSyncJobsRepository syncJobRepository,
                              IGraphGroupRepository graphGroupRepository,
                              TelemetryClient telemetryClient,
                              IThresholdNotificationService thresholdNotificationService) : base(loggingRepository)
        {
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _thresholdNotificationService = thresholdNotificationService ?? throw new ArgumentNullException(nameof(thresholdNotificationService));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        protected override async Task<ResolveNotificationResponse> ExecuteCoreAsync(ResolveNotificationRequest request)
        {
            var response = new ResolveNotificationResponse();
            var thresholdNotification = await _notificationRepository.GetThresholdNotificationByIdAsync(request.Id);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"ResolveNotificationHandler request: " +
                $"Id: {request.Id}, UserUPN: {request.UserUPN}, TargetOfficeGroupId: {thresholdNotification?.TargetOfficeGroupId}"
            });
            if (thresholdNotification == null)
            {
                response.CardJson = _thresholdNotificationService.CreateNotFoundNotificationCard(request.Id);
                return response;
            }

            var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserUPN, thresholdNotification.TargetOfficeGroupId);
            if (!isGroupOwner)
            {
                response.CardJson = await _thresholdNotificationService.CreateUnauthorizedNotificationCardAsync(thresholdNotification);
                return response;
            }

            if (thresholdNotification.Status != ThresholdNotificationStatus.Resolved)
            {
                var resolution = Enum.Parse<ThresholdNotificationResolution>(request.Resolution);
                thresholdNotification.Status = ThresholdNotificationStatus.Resolved;
                thresholdNotification.CardState = ThresholdNotificationCardState.NoCard;
                thresholdNotification.Resolution = resolution;
                thresholdNotification.ResolvedByUPN = request.UserUPN;
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
            var job = await _syncJobRepository.GetSyncJobAsync(notification.SyncJobId);

            if (notification.Resolution == ThresholdNotificationResolution.IgnoreOnce)
            {
                job.IgnoreThresholdOnce = true;
                job.Status = SyncStatus.Idle.ToString();
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { job }, SyncStatus.Idle);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resolved Notification. Setting the status of the sync back to Idle."});
            }
            else if (notification.Resolution == ThresholdNotificationResolution.Paused)
            {
                job.Status = SyncStatus.CustomerPaused.ToString();
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { job }, SyncStatus.CustomerPaused);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resolved Notification. Setting the status of the sync to CustomerPaused."});
            }
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