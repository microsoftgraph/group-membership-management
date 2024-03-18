// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Newtonsoft.Json;
using System.Text;
using System.Threading.Tasks;
using System;
using Entities;
using Models;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Azure.Messaging.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;

namespace Hosts.GroupOwnershipObtainer
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly bool _isDryRunEnabled;

        public StarterFunction(ILoggingRepository loggingRepository, IDatabaseSyncJobsRepository syncJobRepository, IDryRunValue dryRun)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _isDryRunEnabled = dryRun != null ? dryRun.DryRunEnabled : throw new ArgumentNullException(nameof(dryRun));
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task RunAsync(
        [ServiceBusTrigger("%serviceBusSyncJobTopic%", "GroupOwnership", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
        [DurableClient] IDurableOrchestrationClient starter)
        {
            var syncJob = JsonConvert.DeserializeObject<SyncJob>(Encoding.UTF8.GetString(message.Body));
            var runId = syncJob.RunId.GetValueOrDefault(Guid.Empty);

            _loggingRepository.SetSyncJobProperties(runId, syncJob.ToDictionary());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            if ((DateTime.UtcNow - syncJob.DryRunTimeStamp) < TimeSpan.FromHours(syncJob.Period) && _isDryRunEnabled)
            {
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, SyncStatus.Idle);
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Setting the status of the sync back to Idle as the sync has run within the previous DryRunTimeStamp period",
                    RunId = runId
                });
            }
            else
            {
                var request = new OrchestratorRequest
                {
                    SyncJob = syncJob,
                    Exclusionary = message.ApplicationProperties.ContainsKey("Exclusionary") ? Convert.ToBoolean(message.ApplicationProperties["Exclusionary"]) : false,
                    CurrentPart = message.ApplicationProperties.ContainsKey("CurrentPart") ? Convert.ToInt32(message.ApplicationProperties["CurrentPart"]) : 0,
                    TotalParts = message.ApplicationProperties.ContainsKey("TotalParts") ? Convert.ToInt32(message.ApplicationProperties["TotalParts"]) : 0
                };

                var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), request);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId} for job RowKey: {syncJob.RowKey}", RunId = runId });
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}
