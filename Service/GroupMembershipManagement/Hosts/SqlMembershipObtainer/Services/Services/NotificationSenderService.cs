// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services
{
    public class NotificationSenderService : INotificationSenderService
    {
        private readonly IServiceBusQueueRepository _notificationsQueueRepository;
        private readonly ILogger<NotificationSenderService> _logger;

        public NotificationSenderService(
            IServiceBusQueueRepository notificationsQueueRepository,
            ILogger<NotificationSenderService> logger)
        {
            _notificationsQueueRepository = notificationsQueueRepository ?? throw new ArgumentNullException(nameof(notificationsQueueRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters)
        {
            var messageContent = new Dictionary<string, object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParameters }
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));

            // The MessageId makes the send idempotent across Durable activity replays via the queue's duplicate detection.
            var message = new ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());

            await _notificationsQueueRepository.SendMessageAsync(message);

            _logger.LogInformation(
                "Queued {NotificationType} notification {MessageId} for job {JobId}.",
                notificationType,
                message.MessageId,
                job.Id);
        }
    }
}
