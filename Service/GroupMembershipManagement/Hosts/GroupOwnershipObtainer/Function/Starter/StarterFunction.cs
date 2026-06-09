// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models;
using Models.SyncJobHistory;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly ISyncJobStatusService _syncJobStatusService;
        private readonly bool _isDryRunEnabled;

        public StarterFunction(
            ILogger<StarterFunction> logger,
            ISyncJobStatusService syncJobStatusService,
            IDryRunValue dryRun)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
            _isDryRunEnabled = dryRun != null ? dryRun.DryRunEnabled : throw new ArgumentNullException(nameof(dryRun));
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobTopic%", "GroupOwnership", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient starter)
        {
            var syncJob = JsonSerializer.Deserialize<SyncJob>(Encoding.UTF8.GetString(message.Body));
            var currentPart = message.ApplicationProperties.ContainsKey("CurrentPart") ? Convert.ToInt32(message.ApplicationProperties["CurrentPart"]) : 0;
            var totalParts = message.ApplicationProperties.ContainsKey("TotalParts") ? Convert.ToInt32(message.ApplicationProperties["TotalParts"]) : 0;
            using var scope = _logger.BeginSyncJobScope(syncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = currentPart,
                ["TotalParts"] = totalParts
            });

            _logger.FunctionStarted(nameof(StarterFunction));

            if ((DateTime.UtcNow - syncJob.DryRunTimeStamp) < TimeSpan.FromHours(syncJob.Period) && _isDryRunEnabled)
            {
                syncJob.Status = SyncStatus.Idle.ToString();

                var now = DateTime.UtcNow;
                var updateBy = nameof(Hosts.GroupOwnershipObtainer);
                var history = new SyncJobHistory
                {
                    SyncJobId = syncJob.Id,
                    RunId = syncJob.RunId ?? Guid.Empty,
                    Status = SyncStatus.Idle.ToString(),
                    EndTime = syncJob.Status != SyncStatus.InProgress.ToString() ? now : null,
                    UpdatedByFunction = updateBy,
                    UpdatedAt = now
                };

                await _syncJobStatusService.UpdateJobStatusAsync(syncJob, SyncStatus.Idle, history, functionName: updateBy);
                _logger.DryRunIdleStatus();
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
                _logger.OrchestrationInstanceStarted(instanceId, syncJob.Id);
            }

            _logger.FunctionCompleted(nameof(StarterFunction));
        }
    }
}
