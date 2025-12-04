// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
namespace Hosts.Notifier
{
    public class StarterFunction
    {
        private readonly IMailConfig _mailConfig;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IThresholdNotificationConfig _thresholdNotificationConfig;

        public StarterFunction(ILoggingRepository loggingRepository, IThresholdNotificationConfig thresholdNotificationConfig, IMailConfig mailConfig)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _thresholdNotificationConfig = thresholdNotificationConfig ?? throw new ArgumentNullException(nameof(thresholdNotificationConfig));
            _mailConfig = mailConfig ?? throw new ArgumentNullException(nameof(mailConfig)); ;
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusNotificationsQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);
            string messageBody = Encoding.UTF8.GetString(message.Body.ToArray());
            string messageType = message.ApplicationProperties.ContainsKey("MessageType")
                                    ? message.ApplicationProperties["MessageType"].ToString()
                                    : "Unknown";

            var orchestratorRequest = new OrchestratorRequest
            {
                MessageBody = messageBody,
                MessageType = messageType
            };
            var messageContent = JsonSerializer.Deserialize<Dictionary<string, Object>>(messageBody);
            SyncJob job = ((JsonElement)messageContent["SyncJob"]).Deserialize<SyncJob>();
            Guid runId = job.RunId ?? Guid.Empty;
            _loggingRepository.SetSyncJobProperties(runId, job.ToDictionary());

            if (_mailConfig.SkipEmailNotifications)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = "Email notifications are disabled."
                });

            }
            else
            {
                var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), orchestratorRequest);
            }



            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}
