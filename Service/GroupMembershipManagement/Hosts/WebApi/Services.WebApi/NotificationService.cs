// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.WebApi.Contracts;
using System.Text.Json;

namespace Services.WebApi
{
    public class NotificationService : INotificationService
    {
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;
        private readonly ILogger<NotificationService> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;

        public NotificationService(
            [FromKeyedServices("Notifications")] IServiceBusQueueRepository serviceBusQueueRepository,
            ILogger<NotificationService> logger,
            IGraphGroupRepository graphGroupRepository)
        {
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        public async Task SendSubmissionRejectedNotificationAsync(
            SyncJob syncJob,
            SyncJobChange submission)
        {
            await SendReviewStatusChangeNotificationAsync(syncJob, submission, NotificationMessageType.SubmissionRejectedNotification);
        }

        public async Task SendSubmissionApprovedNotificationAsync(
            SyncJob syncJob,
            SyncJobChange submission)
        {
            await SendReviewStatusChangeNotificationAsync(syncJob, submission, NotificationMessageType.SubmissionApprovedNotification);
        }

        public async Task SendReviewStatusChangeNotificationAsync(
            SyncJob syncJob,
            SyncJobChange submission,
            NotificationMessageType notificationType)
        {
            string groupName;
            try
            {
                groupName = await _graphGroupRepository.GetGroupNameAsync(syncJob.TargetOfficeGroupId);
                if (string.IsNullOrEmpty(groupName))
                {
                    groupName = "<Group name could not be retrieved>";
                }
            }
            catch (Exception ex)
            {
                groupName = "<Group name could not be retrieved>";
                _logger.NotificationGroupNameRetrievalFailed(syncJob.RunId, syncJob.TargetOfficeGroupId, ex);
            }

            var isRejection = notificationType == NotificationMessageType.SubmissionRejectedNotification;
            var businessJustification = isRejection ? submission.BusinessJustification ?? "No reason provided" : null;

            var additionalContentParameters = isRejection
                ? new string[]
                {
                    syncJob.TargetOfficeGroupId.ToString(),                      // {0} - Group ID
                    groupName,                                                     // {1} - Group Name
                    businessJustification!,                                        // {2} - Rejection Reason
                    syncJob.Requestor ?? string.Empty,                            // {3} - Requestor email
                    DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture) // {4} - RejectedAtUtc (ISO 8601)
                }
                : new string[]
                {
                    syncJob.TargetOfficeGroupId.ToString(),  // {0} - Group ID
                    groupName                                // {1} - Group Name
                };

            var customProperties = new Dictionary<string, object>
            {
                { "AdditionalContentParameters", additionalContentParameters },
                { "SubmitterObjectId", submission.ChangedByObjectId?.ToString() ?? string.Empty },
                { "SubmitterDisplayName", submission.ChangedByDisplayName ?? string.Empty }
            };

            if (isRejection)
            {
                customProperties.Add("BusinessJustification", businessJustification!);
            }

            await SendNotificationAsync(syncJob, notificationType, customProperties);
        }

        public async Task SendNotificationAsync(
            SyncJob syncJob,
            NotificationMessageType notificationType,
            Dictionary<string, object>? customProperties = null)
        {
            try
            {
                var messageContent = new Dictionary<string, object>
                {
                    { "SyncJob", syncJob }
                };

                if (customProperties != null)
                {
                    foreach (var property in customProperties)
                    {
                        if (!messageContent.ContainsKey(property.Key))
                        {
                            messageContent[property.Key] = property.Value;
                        }
                        else
                        {
                            // Handle the conflict, e.g., log a warning or throw an exception
                            _logger.NotificationMessageContentKeyConflict(syncJob.RunId, property.Key);
                        }
                    }
                }

                var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
                var messageId = $"{syncJob.Id}_{syncJob.RunId}_{notificationType}";

                var message = new ServiceBusMessage
                {
                    MessageId = messageId,
                    Body = body
                };
                message.ApplicationProperties.Add("MessageType", notificationType.ToString());

                await _serviceBusQueueRepository.SendMessageAsync(message);

                _logger.NotificationMessageSent(syncJob.RunId, messageId, notificationType);
            }
            catch (Exception ex)
            {
                _logger.NotificationSendFailed(syncJob.RunId, notificationType, ex);
                throw;
            }
        }
    }
}
