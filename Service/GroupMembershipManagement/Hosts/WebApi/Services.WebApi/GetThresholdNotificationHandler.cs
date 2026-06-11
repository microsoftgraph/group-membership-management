// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;

namespace Services
{
    public class GetThresholdNotificationHandler : RequestHandlerBase<GetThresholdNotificationRequest, GetThresholdNotificationResponse>
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly ILoggingRepository _loggingRepository;

        public GetThresholdNotificationHandler(ILoggingRepository loggingRepository,
                                               INotificationRepository notificationRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        }

        protected override async Task<GetThresholdNotificationResponse> ExecuteCoreAsync(GetThresholdNotificationRequest request)
        {
            var response = new GetThresholdNotificationResponse();

            try
            {
                var notification = await _notificationRepository.GetThresholdNotificationBySyncJobIdAsync(request.SyncJobId);

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
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error getting threshold notification for SyncJobId {request.SyncJobId}: {ex.Message}"
                });

                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }
    }
}
