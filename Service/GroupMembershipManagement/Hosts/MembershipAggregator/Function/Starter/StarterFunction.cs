// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName("ServiceBusStarterFunction")]
        public async Task ProcessServiceBusMessageAsync(
            [ServiceBusTrigger("%serviceBusMembershipAggregatorQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            var request = JsonConvert.DeserializeObject<MembershipAggregatorHttpRequest>(Encoding.UTF8.GetString(message.Body));
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            _loggingRepository.SetSyncJobProperties(runId, request.SyncJob.ToDictionary());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Processing message {message.MessageId}", RunId = runId }, VerbosityLevel.INFO);

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), request);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId}", RunId = runId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}
