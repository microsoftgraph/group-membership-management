// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Hosts.SqlMembershipObtainer;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;

namespace SqlMembershipObtainer
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService;
        private readonly bool _isSqlMembershipDryRunEnabled;

        public StarterFunction(ILogger<StarterFunction> logger, ISqlMembershipObtainerService sqlMembershipObtainerService, IDryRunValue dryRun)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
            _isSqlMembershipDryRunEnabled = dryRun.DryRunEnabled;
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
        [ServiceBusTrigger("%serviceBusTopicName%", "SqlMembership", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
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
                if ((DateTime.UtcNow - syncJob.DryRunTimeStamp) < TimeSpan.FromHours(syncJob.Period) && _isSqlMembershipDryRunEnabled)
                {
                    await _sqlMembershipObtainerService.UpdateSyncJobStatusToIdleAsync(syncJob);
                    _logger.DryRunIdleStatus();
                    return;
                }

                _logger.FunctionStarted(nameof(StarterFunction));

                var request = new OrchestratorRequest
                {
                    SyncJob = syncJob,
                    Exclusionary = message.ApplicationProperties.ContainsKey("Exclusionary") ? Convert.ToBoolean(message.ApplicationProperties["Exclusionary"]) : false,
                    CurrentPart = currentPart,
                    TotalParts = totalParts,
                };

                var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), request);
                _logger.OrchestrationInstanceStarted(instanceId, syncJob.Id);

                _logger.FunctionCompleted(nameof(StarterFunction));
            }
        }
    }
}
