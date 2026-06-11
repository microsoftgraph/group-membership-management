// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models.ServiceBus;
using Repositories.Contracts.Helpers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.SyncJobUpdater
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;

        public StarterFunction(ILogger<StarterFunction> logger)
        {
            _logger = logger;
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobUpdaterQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient starter)
        {
            var body = Encoding.UTF8.GetString(message.Body.ToArray());
            var updateRequest = JsonSerializer.Deserialize<JobStatusUpdateQueueMessage>(body) ?? throw new InvalidOperationException("Failed to deserialize JobStatusUpdateQueueMessage.");
            
            using (_logger.BeginSyncJobScope(updateRequest.SyncJob))
            {
                _logger.FunctionStarted(nameof(StarterFunction));

                var request = new OrchestratorRequest
                {
                    Message = updateRequest
                };

                var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), request);
                _logger.OrchestratorInstanceCreated(instanceId, updateRequest.JobId);
                
                _logger.FunctionCompleted(nameof(StarterFunction));
            }
        }
    }
}