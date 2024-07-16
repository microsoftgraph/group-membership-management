// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Hosts.JobFInalizer;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.JobFinalizer
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
            [ServiceBusTrigger("%serviceBusSyncJobTopic%", "SyncJobStatus", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            var syncJob = JsonSerializer.Deserialize<SyncJob>(Encoding.UTF8.GetString(message.Body));
            var runId = syncJob.RunId.GetValueOrDefault(Guid.Empty);
            _loggingRepository.SetSyncJobProperties(runId, syncJob.ToDictionary());

            string syncjobStatus = message.ApplicationProperties.ContainsKey("SyncjobStatus")
                                    ? message.ApplicationProperties["SyncjobStatus"].ToString()
                                    : "Unknown";
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            var request = new OrchestratorRequest
                {
                    SyncJob = syncJob,
                    Status = syncjobStatus
                };

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), request);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId} for job Id: {syncJob.Id} ", RunId = runId });
            

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}