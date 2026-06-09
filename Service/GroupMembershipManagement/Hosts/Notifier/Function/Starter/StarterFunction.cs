// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Services.Notifier;
using System;
using System.Text;
using System.Threading.Tasks;
namespace Hosts.Notifier
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly IMailConfig _mailConfig;
        private readonly IThresholdNotificationConfig _thresholdNotificationConfig;

        public StarterFunction(ILogger<StarterFunction> logger, IThresholdNotificationConfig thresholdNotificationConfig, IMailConfig mailConfig)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _thresholdNotificationConfig = thresholdNotificationConfig ?? throw new ArgumentNullException(nameof(thresholdNotificationConfig));
            _mailConfig = mailConfig ?? throw new ArgumentNullException(nameof(mailConfig));
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusNotificationsQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient starter)
        {
            string messageBody = Encoding.UTF8.GetString(message.Body.ToArray());
            string messageType = message.ApplicationProperties.ContainsKey("MessageType")
                                    ? message.ApplicationProperties["MessageType"].ToString()
                                    : "Unknown";

            var orchestratorRequest = new OrchestratorRequest
            {
                MessageBody = messageBody,
                MessageType = messageType
            };
            var messageContent = NotificationMessageContentParser.ParseMessageBody(messageBody);
            var job = NotificationMessageContentParser.GetRequiredValue<SyncJob>(messageContent, "SyncJob");

            using var scope = _logger.BeginSyncJobScope(job);

            _logger.FunctionStarted(nameof(StarterFunction));

            if (_mailConfig.SkipEmailNotifications)
            {
                _logger.EmailNotificationsDisabled();
            }
            else
            {
                var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), orchestratorRequest);
            }

            _logger.FunctionCompleted(nameof(StarterFunction));
        }
    }
}
