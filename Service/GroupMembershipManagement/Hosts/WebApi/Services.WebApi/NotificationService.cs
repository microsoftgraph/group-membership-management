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
using Repositories.Contracts.DestinationResolution;
using Services.WebApi.Contracts;
using System.Text.Json;

namespace Services.WebApi
{
    public class NotificationService : INotificationService
    {
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;
        private readonly ILogger<NotificationService> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDestinationResolver _destinationResolver;

        public NotificationService(
            [FromKeyedServices("Notifications")] IServiceBusQueueRepository serviceBusQueueRepository,
            ILogger<NotificationService> logger,
            IGraphGroupRepository graphGroupRepository,
            IDestinationResolver destinationResolver)
        {
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _destinationResolver = destinationResolver ?? throw new ArgumentNullException(nameof(destinationResolver));
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
            var resolvedDestination = await _destinationResolver.ResolveAsync(syncJob);
            var groupIdentity = ResolveGroupIdentity(syncJob, resolvedDestination);
            try
            {
                groupName = await _graphGroupRepository.GetGroupNameAsync(groupIdentity);
                if (string.IsNullOrEmpty(groupName))
                {
                    groupName = "<Group name could not be retrieved>";
                }
            }
            catch (Exception ex)
            {
                groupName = "<Group name could not be retrieved>";
                _logger.NotificationGroupNameRetrievalFailed(syncJob.RunId, groupIdentity, ex);
            }

            var isRejection = notificationType == NotificationMessageType.SubmissionRejectedNotification;
            var businessJustification = isRejection ? submission.BusinessJustification ?? "No reason provided" : null;

            var additionalContentParameters = isRejection
                ? new string[]
                {
                    groupIdentity.ToString(),                      // {0} - Group ID
                    groupName,                                                     // {1} - Group Name
                    businessJustification!,                                        // {2} - Rejection Reason
                    syncJob.Requestor ?? string.Empty,                            // {3} - Requestor email
                    DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture) // {4} - RejectedAtUtc (ISO 8601)
                }
                : new string[]
                {
                    groupIdentity.ToString(),  // {0} - Group ID
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

            // Give rejections a unique id so two rejections of the same job aren't seen as duplicates and dropped.
            var deduplicationId = isRejection ? Guid.NewGuid().ToString("N").Substring(0, 12) : null;
            await SendNotificationCoreAsync(syncJob, notificationType, customProperties, deduplicationId);
        }

        public Task SendNotificationAsync(
            SyncJob syncJob,
            NotificationMessageType notificationType,
            Dictionary<string, object>? customProperties = null)
            => SendNotificationCoreAsync(syncJob, notificationType, customProperties, deduplicationId: null);

        private async Task SendNotificationCoreAsync(
            SyncJob syncJob,
            NotificationMessageType notificationType,
            Dictionary<string, object>? customProperties,
            string? deduplicationId)
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
                // Rejections append a short unique suffix so distinct sends get a unique MessageId (within the 128-char limit).
                var messageId = string.IsNullOrEmpty(deduplicationId)
                    ? $"{syncJob.Id}_{syncJob.RunId}_{notificationType}"
                    : $"{syncJob.Id}_{syncJob.RunId}_{notificationType}_{deduplicationId}";

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

        // Explicitly handles both destination kinds per the resolver contract (ResolvedGroupDestination.ObjectId,
        // ResolvedTeamsChannelDestination.TeamObjectId) rather than collapsing to a shared alias. Falls back to the
        // legacy persisted scalar only when the boundary cannot resolve (preserves pre-migration behavior exactly).
        private static Guid ResolveGroupIdentity(SyncJob syncJob, ResolvedDestination? resolvedDestination)
        {
            return resolvedDestination switch
            {
                ResolvedGroupDestination group => group.ObjectId,
                ResolvedTeamsChannelDestination channel => channel.TeamObjectId,
                _ => syncJob.TargetOfficeGroupId
            };
        }
    }
}
