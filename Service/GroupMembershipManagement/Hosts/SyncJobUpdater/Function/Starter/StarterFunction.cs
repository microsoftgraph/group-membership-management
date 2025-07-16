// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.SyncJobUpdater
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository;
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobUpdaterQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            var updateRequest = JsonSerializer.Deserialize<JobStatusUpdateQueueMessage>(Encoding.UTF8.GetString(message.Body));
            var runId = updateRequest.RunId;
            _loggingRepository.SetSyncJobProperties(runId, updateRequest.SyncJob.ToDictionary());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            var request = new OrchestratorRequest
            {
                Message = updateRequest
            };

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), request);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId} for job Id: {updateRequest.JobId} ", RunId = runId });
            
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}