// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;

namespace Hosts.SyncJobUpdater
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository;
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobUpdaterQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient client)
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

            var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), request);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId} for job Id: {syncJob.Id} ", RunId = runId });
            

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}