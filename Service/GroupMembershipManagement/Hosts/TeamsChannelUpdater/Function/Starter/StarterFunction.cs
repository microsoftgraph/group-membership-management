// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;

namespace Hosts.TeamsChannelUpdater
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
        public async Task RunAsync(
             [ServiceBusTrigger("%serviceBusMembershipUpdatersTopic%", "TeamsChannelUpdater", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
             [DurableClient] IDurableOrchestrationClient starter)
        {
            var request = JsonConvert.DeserializeObject<MembershipHttpRequest>(Encoding.UTF8.GetString(message.Body));
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            _loggingRepository.SetSyncJobProperties(runId, request.SyncJob.ToDictionary());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Processing message {message.MessageId}", RunId = runId }, VerbosityLevel.INFO);

            var instanceId = await starter.StartNewAsync(nameof(MessageOrchestrator), request);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId}", RunId = runId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}
