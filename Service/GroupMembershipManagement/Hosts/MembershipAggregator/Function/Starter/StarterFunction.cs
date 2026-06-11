// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;

        public StarterFunction(ILogger<StarterFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function("ServiceBusStarterFunction")]
        public async Task ProcessServiceBusMessageAsync(
            [ServiceBusTrigger("%serviceBusMembershipAggregatorQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient starter)
        {
            var request = JsonSerializer.Deserialize<MembershipAggregatorHttpRequest>(Encoding.UTF8.GetString(message.Body));

            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.PartNumber,
                ["TotalParts"] = request.PartsCount
            }))
            {
                _logger.FunctionStarted(nameof(StarterFunction));
                _logger.ProcessingMessage(message.MessageId);

                var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), request);

                _logger.OrchestrationInstanceStarted(instanceId);
                _logger.FunctionCompleted(nameof(StarterFunction));
            }
        }
    }
}
