// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Services.Notifier;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
namespace Hosts.Notifier
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly IMailConfig _mailConfig;
        private readonly IThresholdNotificationConfig _thresholdNotificationConfig;
        private readonly INotificationTypesRepository _notificationTypesRepository;
        private readonly IDeferredNotificationsRepository _deferredNotificationsRepository;
        private readonly TelemetryClient _telemetryClient;

        public StarterFunction(
            ILogger<StarterFunction> logger,
            IThresholdNotificationConfig thresholdNotificationConfig,
            IMailConfig mailConfig,
            INotificationTypesRepository notificationTypesRepository,
            IDeferredNotificationsRepository deferredNotificationsRepository,
            TelemetryClient telemetryClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _thresholdNotificationConfig = thresholdNotificationConfig ?? throw new ArgumentNullException(nameof(thresholdNotificationConfig));
            _mailConfig = mailConfig ?? throw new ArgumentNullException(nameof(mailConfig));
            _notificationTypesRepository = notificationTypesRepository ?? throw new ArgumentNullException(nameof(notificationTypesRepository));
            _deferredNotificationsRepository = deferredNotificationsRepository ?? throw new ArgumentNullException(nameof(deferredNotificationsRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusNotificationsTopic%", "%serviceBusNotificationsSubscription%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions,
            [DurableClient] DurableTaskClient starter)
        {
            string messageBody = Encoding.UTF8.GetString(message.Body.ToArray());
            string messageType = message.ApplicationProperties.ContainsKey("MessageType")
                                    ? message.ApplicationProperties["MessageType"].ToString()
                                    : "Unknown";

            var messageContent = NotificationMessageContentParser.ParseMessageBody(messageBody);
            var job = NotificationMessageContentParser.GetRequiredValue<SyncJob>(messageContent, "SyncJob");

            using var scope = _logger.BeginSyncJobScope(job);

            _logger.FunctionStarted(nameof(StarterFunction));

            if (Enum.TryParse<NotificationMessageType>(messageType, true, out var notificationMessageType))
            {
                var notificationType = await _notificationTypesRepository.GetNotificationTypeByNotificationTypeNameAsync(notificationMessageType);
                if (notificationType != null && notificationType.Disabled)
                {
                    await DeferMessageAsync(message, messageActions, notificationMessageType, job, "NotificationTypeDisabled");
                    return;
                }
            }

            if (_mailConfig.SkipEmailNotifications)
            {
                _logger.EmailNotificationsDisabled();

                if (Enum.TryParse<NotificationMessageType>(messageType, true, out var skippedType))
                {
                    await DeferMessageAsync(message, messageActions, skippedType, job, "SkipEmailNotifications");
                }
                else
                {
                    // Unknown message type with global skip — defer without tracking
                    await messageActions.DeferMessageAsync(message);
                }

                _logger.FunctionCompleted(nameof(StarterFunction));
                return;
            }

            var orchestratorRequest = new OrchestratorRequest
            {
                MessageBody = messageBody,
                MessageType = messageType
            };

            await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), orchestratorRequest);

            await messageActions.CompleteMessageAsync(message);
            _logger.FunctionCompleted(nameof(StarterFunction));
        }

        private async Task DeferMessageAsync(
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions,
            NotificationMessageType messageType,
            SyncJob job,
            string reason)
        {
            _logger.NotificationTypeSuppressed(messageType.ToString(), message.SequenceNumber);

            // Write tracking row FIRST (DB-first pattern for idempotency).
            // If this succeeds but SB deferral fails, the function will retry and the
            // idempotent AddDeferredNotificationAsync will skip the duplicate insert.
            await _deferredNotificationsRepository.AddDeferredNotificationAsync(new DeferredNotification
            {
                SequenceNumber = message.SequenceNumber,
                MessageType = messageType,
                Status = DeferredNotificationStatus.Deferred,
                DeferredAt = DateTime.UtcNow,
                MessageExpiresAt = message.ExpiresAt.UtcDateTime,
                SuppressionReason = reason,
                SyncJobId = job.Id,
                RunId = job.RunId
            });

            await messageActions.DeferMessageAsync(message);

            _telemetryClient.TrackEvent("NotificationDeferred", new Dictionary<string, string>
            {
                { "MessageType", messageType.ToString() },
                { "SyncJobId", job.Id.ToString() },
                { "Reason", reason },
                { "ExpiresAt", message.ExpiresAt.UtcDateTime.ToString("O") }
            });

            _logger.FunctionCompleted(nameof(StarterFunction));
        }
    }
}
