// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly bool _isPlaceMembershipObtainerDryRunEnabled;

        public StarterFunction(ILogger<StarterFunction> logger, IDatabaseSyncJobsRepository syncJobRepository, IDryRunValue dryRun)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _isPlaceMembershipObtainerDryRunEnabled = dryRun.DryRunEnabled;
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
           [ServiceBusTrigger("%serviceBusSyncJobTopic%", "PlaceMembership", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
           [DurableClient] DurableTaskClient starter)
        {
            var syncJob = JsonSerializer.Deserialize<SyncJob>(Encoding.UTF8.GetString(message.Body));
            var currentPart = message.ApplicationProperties.ContainsKey("CurrentPart") ? Convert.ToInt32(message.ApplicationProperties["CurrentPart"]) : 1;
            var totalParts = message.ApplicationProperties.ContainsKey("TotalParts") ? Convert.ToInt32(message.ApplicationProperties["TotalParts"]) : 1;

            using (_logger.BeginSyncJobScope(syncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = currentPart,
                ["TotalParts"] = totalParts
            }))
            {
                _logger.FunctionStarted(nameof(StarterFunction));

                if ((DateTime.UtcNow - syncJob.DryRunTimeStamp) < TimeSpan.FromHours(syncJob.Period) && _isPlaceMembershipObtainerDryRunEnabled == true)
                {
                    await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, SyncStatus.Idle);
                    _logger.SettingStatusToIdle();
                }
                else
                {
                    var request = new OrchestratorRequest
                    {
                        SyncJob = syncJob,
                        Exclusionary = message.ApplicationProperties.ContainsKey("Exclusionary") ? Convert.ToBoolean(message.ApplicationProperties["Exclusionary"]) : false,
                        CurrentPart = currentPart,
                        TotalParts = totalParts
                    };

                    var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), request);
                    _logger.InstanceIdCreated(instanceId, syncJob.Id);
                }

                _logger.FunctionCompleted(nameof(StarterFunction));
            }
        }
    }
}

