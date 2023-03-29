// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.VisualBasic;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class ResolveNotificationHandler : RequestHandlerBase<ResolveNotificationRequest, ResolveNotificationResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly INotificationRepository _notificationRepository;

        public ResolveNotificationHandler(ILoggingRepository loggingRepository,
                              INotificationRepository notificationRepository,
                              IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<ResolveNotificationResponse> ExecuteCoreAsync(ResolveNotificationRequest request)
        {
            var response = new ResolveNotificationResponse();
            var thresholdNotification = await _notificationRepository.GetThresholdNotificationByIdAsync(request.Id);

            if (thresholdNotification == null)
            {
                // TODO: Will return actual card json in user story: 10204885.
                response.CardJson = "Error: Notification not found.";
                return response;
            }

            var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserUPN, thresholdNotification.TargetOfficeGroupId);

            if (!isGroupOwner)
            {
                // TODO: Will return actual card json in user story: 10204885.
                response.CardJson = "Error: User is not an owner.";
                return response;
            }

            if (thresholdNotification.Status == ThresholdNotificationStatus.Resolved)
            {
                // TODO: Will return actual card json in user story: 10204885.
                response.CardJson = "Notification has already been resolved.";
                return response;
            }

            var resolution = Enum.Parse<ThresholdNotificationResolution>(request.Resolution);
            thresholdNotification.Status = ThresholdNotificationStatus.Resolved;
            thresholdNotification.Resolution = resolution;
            thresholdNotification.ResolvedByUPN = request.UserUPN;
            thresholdNotification.ResolvedTime = DateTime.UtcNow;
            await _notificationRepository.SaveNotificationAsync(thresholdNotification);

            // TODO: Will update the sync job repository (Pause or Set the override bit)
            // in user story: 10204885.

            // TODO: Will return actual card json in user story: 10204885.
            response.CardJson = "Notification has now been resolved.";
            return response;
        }
    }
}