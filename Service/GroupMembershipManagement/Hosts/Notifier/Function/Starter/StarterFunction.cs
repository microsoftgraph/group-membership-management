// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Azure.Messaging.ServiceBus;
using System.Text;
namespace Hosts.Notifier
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IThresholdNotificationConfig _thresholdNotificationConfig;

        public StarterFunction(ILoggingRepository loggingRepository, IThresholdNotificationConfig thresholdNotificationConfig)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _thresholdNotificationConfig = thresholdNotificationConfig ?? throw new ArgumentNullException(nameof(thresholdNotificationConfig));
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task ProcessServiceBusMessageAsync(
            [ServiceBusTrigger("%serviceBusNotificationsQueue%", Connection = "serviceBusConnectionString")] ServiceBusReceivedMessage message,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);
            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), (message));
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}
