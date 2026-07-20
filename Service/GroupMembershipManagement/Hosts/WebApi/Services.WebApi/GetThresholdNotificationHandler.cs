// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Models;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;

namespace Services
{
    public class GetThresholdNotificationHandler : RequestHandlerBase<GetThresholdNotificationRequest, GetThresholdNotificationResponse>
    {
        private readonly ILogger<GetThresholdNotificationHandler> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly IHandleInactiveJobsConfig _handleInactiveJobsConfig;

        public GetThresholdNotificationHandler(ILogger<GetThresholdNotificationHandler> logger,
                                               INotificationRepository notificationRepository,
                                               IHandleInactiveJobsConfig handleInactiveJobsConfig) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _handleInactiveJobsConfig = handleInactiveJobsConfig ?? throw new ArgumentNullException(nameof(handleInactiveJobsConfig));
        }

        protected override async Task<GetThresholdNotificationResponse> ExecuteCoreAsync(GetThresholdNotificationRequest request)
        {
            var response = new GetThresholdNotificationResponse();

            try
            {
                var notification = await _notificationRepository.GetThresholdNotificationBySyncJobIdAsync(request.SyncJobId);

                if (notification == null)
                {
                    // No open alert: return the latest (maybe already-resolved) one so the UI can show its resolved state (FR-014).
                    notification = await _notificationRepository.GetLatestThresholdNotificationBySyncJobIdAsync(request.SyncJobId);
                }

                if (notification == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    return response;
                }

                response.NotificationId = notification.Id;
                response.ChangeQuantityForAdditions = notification.ChangeQuantityForAdditions;
                response.ChangePercentageForAdditions = notification.ChangePercentageForAdditions;
                response.ThresholdPercentageForAdditions = notification.ThresholdPercentageForAdditions;
                response.ChangeQuantityForRemovals = notification.ChangeQuantityForRemovals;
                response.ChangePercentageForRemovals = notification.ChangePercentageForRemovals;
                response.ThresholdPercentageForRemovals = notification.ThresholdPercentageForRemovals;
                response.PurgeDate = notification.LastUpdatedTime.AddDays(_handleInactiveJobsConfig.NumberOfDaysBeforePurging);

                response.IsResolved = notification.Status == ThresholdNotificationStatus.Resolved;
                if (response.IsResolved)
                {
                    response.ResolvedBy = notification.ResolvedBy;
                    response.ResolvedTime = notification.ResolvedTime;
                    response.Resolution = notification.Resolution.ToString();
                }

                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                _logger.ThresholdNotificationRetrievalFailed(request.SyncJobId, ex);
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }
    }
}
