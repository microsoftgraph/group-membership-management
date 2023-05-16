// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Services.Contracts;
using Services.Contracts.Notifications;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services.WebApi
{
    /// <summary>
    /// Handles requests to retrieve a refresh card for a notification.
    /// </summary>
    public class NotificationCardHandler : RequestHandlerBase<NotificationCardRequest, NotificationCardResponse>
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IThresholdNotificationService _thresholdNotificationService;

        public NotificationCardHandler(ILoggingRepository loggingRepository,
            INotificationRepository notificationRepository,
            IGraphGroupRepository graphGroupRepository,
            IThresholdNotificationService thresholdNotificationService) : base(loggingRepository)
        {
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _thresholdNotificationService = thresholdNotificationService ?? throw new ArgumentNullException(nameof(thresholdNotificationService));
        }

        protected override async Task<NotificationCardResponse> ExecuteCoreAsync(NotificationCardRequest request)
        {
            var response = new NotificationCardResponse();
            var notification = await _notificationRepository.GetThresholdNotificationByIdAsync(request.Id);

            if (notification == null)
            {
                // Not Found
                response.CardJson = _thresholdNotificationService.CreateNotFoundNotificationCard(request.Id);
                return response;
            }

            var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserUPN, notification.TargetOfficeGroupId);
            if (!isGroupOwner)
            {
                // Check if user is in the list of GMM Admins


                // Unauthorized
                response.CardJson = await _thresholdNotificationService.CreateUnauthorizedNotificationCardAsync(notification);
            }
            else
            {
                if (notification.Status == ThresholdNotificationStatus.Resolved)
                {
                    // Resolved
                    response.CardJson = await _thresholdNotificationService.CreateResolvedNotificationCardAsync(notification);
                }
                else
                {
                    // Unresolved
                    response.CardJson = await _thresholdNotificationService.CreateNotificationCardAsync(notification);
                }
            }

            return response;
        }
    }
}