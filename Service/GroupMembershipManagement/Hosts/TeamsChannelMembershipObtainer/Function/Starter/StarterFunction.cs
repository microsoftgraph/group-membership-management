// Copyright (c) Microsoft Corporation.
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
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;

        public StarterFunction(ILogger<StarterFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobTopic%", "TeamsChannelMembership", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient starter)
        {
            var channelSyncInfo = new ChannelSyncInfo
            {
                SyncJob = JsonSerializer.Deserialize<SyncJob>(Encoding.UTF8.GetString(message.Body)),
                Exclusionary = message.ApplicationProperties.ContainsKey("Exclusionary") ? Convert.ToBoolean(message.ApplicationProperties["Exclusionary"]) : false,
                CurrentPart = message.ApplicationProperties.ContainsKey("CurrentPart") ? Convert.ToInt32(message.ApplicationProperties["CurrentPart"]) : 0,
                TotalParts = message.ApplicationProperties.ContainsKey("TotalParts") ? Convert.ToInt32(message.ApplicationProperties["TotalParts"]) : 0,
                IsDestinationPart = message.ApplicationProperties.ContainsKey("IsDestinationPart") ? Convert.ToBoolean(message.ApplicationProperties["IsDestinationPart"]) : false,
            };

            using var scope = _logger.BeginSyncJobScope(channelSyncInfo.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = channelSyncInfo.CurrentPart,
                ["TotalParts"] = channelSyncInfo.TotalParts
            });

            _logger.MessageReceived(channelSyncInfo.SyncJob.Query);
            _logger.FunctionStarted(nameof(StarterFunction));

            var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), channelSyncInfo);

            _logger.OrchestratorInstanceStarted(instanceId, channelSyncInfo.SyncJob.RowKey);
            _logger.FunctionCompleted(nameof(StarterFunction));
        }
    }
}