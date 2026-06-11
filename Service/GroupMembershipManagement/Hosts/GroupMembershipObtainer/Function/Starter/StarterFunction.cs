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

namespace Hosts.GroupMembershipObtainer
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly bool _isGroupMembershipDryRunEnabled;

        public StarterFunction(ILogger<StarterFunction> logger, IDatabaseSyncJobsRepository databaseSyncJobsRepository, IDryRunValue dryRun)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository;
            _isGroupMembershipDryRunEnabled = dryRun.DryRunEnabled;
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobTopic%", "GroupMembership", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient starter)
        {
            var syncJob = JsonSerializer.Deserialize<SyncJob>(Encoding.UTF8.GetString(message.Body));
            var currentPart = message.ApplicationProperties.ContainsKey("CurrentPart") ? Convert.ToInt32(message.ApplicationProperties["CurrentPart"]) : 0;
            var totalParts = message.ApplicationProperties.ContainsKey("TotalParts") ? Convert.ToInt32(message.ApplicationProperties["TotalParts"]) : 0;

            using (_logger.BeginSyncJobScope(syncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = currentPart,
                ["TotalParts"] = totalParts
            }))
            {
                _logger.FunctionStarted(nameof(StarterFunction));

                if ((DateTime.UtcNow - syncJob.DryRunTimeStamp) < TimeSpan.FromHours(syncJob.Period) && _isGroupMembershipDryRunEnabled == true)
                {
                    await _databaseSyncJobsRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, SyncStatus.Idle);
                    _logger.DryRunSyncBackToIdle();
                }
                else
                {
                    var request = new OrchestratorRequest
                    {
                        SyncJob = syncJob,
                        Exclusionary = message.ApplicationProperties.ContainsKey("Exclusionary") ? Convert.ToBoolean(message.ApplicationProperties["Exclusionary"]) : false,
                        CurrentPart = currentPart,
                        TotalParts = totalParts,
                        IsDestinationPart = message.ApplicationProperties.ContainsKey("IsDestinationPart") ? Convert.ToBoolean(message.ApplicationProperties["IsDestinationPart"]) : false,
                    };

                    var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), request);
                    _logger.OrchestrationInstanceStarted(instanceId, syncJob.Id);
                }

                _logger.FunctionCompleted(nameof(StarterFunction));
            }
        }
    }
}