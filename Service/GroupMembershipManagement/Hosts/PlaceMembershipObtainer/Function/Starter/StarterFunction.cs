// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Repositories.Contracts.InjectConfig;
using Repositories.Contracts;
using Azure.Messaging.ServiceBus;
using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;

namespace Hosts.PlaceMembershipObtainer
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly bool _isPlaceMembershipObtainerDryRunEnabled;

        public StarterFunction(ILoggingRepository loggingRepository, IDatabaseSyncJobsRepository syncJobRepository, IDryRunValue dryRun)
        {
            _loggingRepository = loggingRepository;
            _syncJobRepository = syncJobRepository;
            _isPlaceMembershipObtainerDryRunEnabled = dryRun.DryRunEnabled;
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
           [ServiceBusTrigger("%serviceBusSyncJobTopic%", "PlaceMembership", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
           [DurableClient] DurableTaskClient client)
        {
            var syncJob = JsonConvert.DeserializeObject<SyncJob>(Encoding.UTF8.GetString(message.Body));
            var runId = syncJob.RunId.GetValueOrDefault(Guid.Empty);
            _loggingRepository.SetSyncJobProperties(runId, syncJob.ToDictionary());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            if ((DateTime.UtcNow - syncJob.DryRunTimeStamp) < TimeSpan.FromHours(syncJob.Period) && _isPlaceMembershipObtainerDryRunEnabled == true)
            {
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, SyncStatus.Idle);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Setting the status of the sync back to Idle as the sync has run within the previous DryRunTimeStamp period", RunId = runId });
            }
            else
            {
                var request = new OrchestratorRequest
                {
                    SyncJob = syncJob,
                    Exclusionary = message.ApplicationProperties.ContainsKey("Exclusionary") ? Convert.ToBoolean(message.ApplicationProperties["Exclusionary"]) : false,
                    CurrentPart = message.ApplicationProperties.ContainsKey("CurrentPart") ? Convert.ToInt32(message.ApplicationProperties["CurrentPart"]) : 1,
                    TotalParts = message.ApplicationProperties.ContainsKey("TotalParts") ? Convert.ToInt32(message.ApplicationProperties["TotalParts"]) : 1 
                };

                var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), request);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId} for job RowKey: {syncJob.RowKey} ", RunId = runId });
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}

