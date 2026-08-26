// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Models;
using Models.SyncJobChange;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using WebApi.Models;

namespace Services
{
    public class ResolveNotificationHandler : RequestHandlerBase<ResolveNotificationRequest, ResolveNotificationResponse>
    {
        private readonly ILogger<ResolveNotificationHandler> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly TelemetryClient _telemetryClient;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ResolveNotificationHandler(ILogger<ResolveNotificationHandler> logger,
                              INotificationRepository notificationRepository,
                              IDatabaseSyncJobsRepository syncJobRepository,
                              ISyncJobChangeRepository syncJobChangeRepository,
                              IGraphGroupRepository graphGroupRepository,
                              TelemetryClient telemetryClient,
                              IHttpContextAccessor httpContextAccessor) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        protected override async Task<ResolveNotificationResponse> ExecuteCoreAsync(ResolveNotificationRequest request)
        {
            var response = new ResolveNotificationResponse();
            var thresholdNotification = await _notificationRepository.GetThresholdNotificationByIdAsync(request.ThresholdNotificationId);

            _logger.ResolveNotificationRequestReceived(request.ThresholdNotificationId, thresholdNotification?.TargetOfficeGroupId);
            if (thresholdNotification == null)
            {
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            // Role check is in-memory, so it runs before the Graph ownership lookup.
            var isTenantWriter = _httpContextAccessor.HttpContext?.User?.IsInRole(Roles.JOB_TENANT_WRITER) ?? false;

            if (!isTenantWriter)
            {
                var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentifier, thresholdNotification.TargetOfficeGroupId);

                if (!isGroupOwner)
                {
                    response.StatusCode = HttpStatusCode.Forbidden;
                    return response;
                }
            }

            if (thresholdNotification.Status != ThresholdNotificationStatus.Resolved)
            {
                var resolvedByValue = request.UserIdentifier;

                if (Guid.TryParse(resolvedByValue, out var userId))
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

            response.StatusCode = HttpStatusCode.OK;
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
                _logger.NotificationResolvedSyncStatusUpdated("Idle");
            }
            else if (notification.Resolution == ThresholdNotificationResolution.Paused)
            {
                job.Status = SyncStatus.CustomerPaused.ToString();
                changeReason = SyncJobChangeReason.StatusUpdate.ToString();
                await _syncJobRepository.UpdateSyncJobFromNotificationAsync(job, SyncStatus.CustomerPaused);
                _logger.NotificationResolvedSyncStatusUpdated("CustomerPaused");
            }

            var syncJobChange = new SyncJobChange
            {
                SyncJobId = notification.SyncJobId,
                ChangeTime = notification.ResolvedTime,
                ChangedByDisplayName = notification.ResolvedBy,
                // Resolutions now occur in the authenticated web UI (deep link), not via OAM email.
                ChangeSource = SyncJobChangeSource.WebApp,
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