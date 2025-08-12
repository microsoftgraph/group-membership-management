// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;
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
        private readonly ILoggingRepository _loggingRepository;

        public NotificationService(
            [FromKeyedServices("Notifications")] IServiceBusQueueRepository serviceBusQueueRepository,
            ILoggingRepository loggingRepository)
        {
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task SendSubmissionRejectedNotificationAsync(
            SyncJob syncJob,
            SyncJobChange submission)
        {
            var businessJustification = submission.BusinessJustification ?? "No reason provided";

            var additionalContentParameters = new string[]
            {
                syncJob.TargetOfficeGroupId.ToString(),  // {0} - Group ID
                string.Empty,                             // {1} - Group Name (will be populated by TryAssignGroupNameAsync)
                businessJustification                     // {2} - Rejection Reason
            };

            var customProperties = new Dictionary<string, object>
            {
                { "AdditionalContentParameters", additionalContentParameters },
                { "SubmitterObjectId", submission.ChangedByObjectId?.ToString() ?? string.Empty },
                { "SubmitterDisplayName", submission.ChangedByDisplayName ?? string.Empty },
                { "BusinessJustification", businessJustification }
            };

            await SendNotificationAsync(syncJob, NotificationMessageType.SubmissionRejectedNotification, customProperties);
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
                            await _loggingRepository.LogMessageAsync(new Models.LogMessage
                            {
                                RunId = syncJob.RunId,
                                Message = $"Key conflict detected: {property.Key} already exists in messageContent and will not be overwritten."
                            });
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

                await _loggingRepository.LogMessageAsync(new Models.LogMessage
                {
                    RunId = syncJob.RunId,
                    Message = $"Sent notification message {messageId} to service bus notifications queue for notification type {notificationType}"
                });
            }
            catch (Exception ex)
            {
                var fullErrorMessage = $"Failed to send notification for type {notificationType}. Error: {ex.Message}";
                if (ex.InnerException != null)
                {
                    fullErrorMessage += $" Inner Exception: {ex.InnerException.Message}";
                }
                // Avoid logging full stack trace to prevent exposure of sensitive information
                _loggingRepository.LogMessageAsync(new Models.LogMessage
                {
                    RunId = syncJob.RunId,
                    Message = $"StackTrace: {ex.StackTrace}"
                });

                await _loggingRepository.LogMessageAsync(new Models.LogMessage
                {
                    RunId = syncJob.RunId,
                    Message = fullErrorMessage
                });

                throw;
            }
        }
    }
}
