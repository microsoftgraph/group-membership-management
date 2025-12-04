// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
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

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobUpdaterQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient starter)
        {
            var body = Encoding.UTF8.GetString(message.Body.ToArray());
            var updateRequest = JsonSerializer.Deserialize<JobStatusUpdateQueueMessage>(body) ?? throw new InvalidOperationException("Failed to deserialize JobStatusUpdateQueueMessage.");
            var runId = updateRequest.RunId;
            if (updateRequest.SyncJob != null)
            {
                _loggingRepository.SetSyncJobProperties(runId, updateRequest.SyncJob.ToDictionary());
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            var request = new OrchestratorRequest
            {
                Message = updateRequest
            };

            var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), request);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId} for job Id: {updateRequest.JobId} ", RunId = runId });
            
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}