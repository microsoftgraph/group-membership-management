// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly bool _isGroupMembershipDryRunEnabled;

        public StarterFunction(ILoggingRepository loggingRepository, IDatabaseSyncJobsRepository databaseSyncJobsRepository, IDryRunValue dryRun)
        {
            _loggingRepository = loggingRepository;
            _databaseSyncJobsRepository = databaseSyncJobsRepository;
            _isGroupMembershipDryRunEnabled = dryRun.DryRunEnabled;
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusSyncJobTopic%", "GroupMembership", Connection = "serviceBusTopicConnection")] ServiceBusReceivedMessage message,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            var syncJob = JsonConvert.DeserializeObject<SyncJob>(Encoding.UTF8.GetString(message.Body));
            var runId = syncJob.RunId.GetValueOrDefault(Guid.Empty);
            _loggingRepository.SetSyncJobProperties(runId, syncJob.ToDictionary());

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            if ((DateTime.UtcNow - syncJob.DryRunTimeStamp) < TimeSpan.FromHours(syncJob.Period) && _isGroupMembershipDryRunEnabled == true)
            {
                await _databaseSyncJobsRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, SyncStatus.Idle);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Setting the status of the sync back to Idle as the sync has run within the previous DryRunTimeStamp period", RunId = runId });
            }
            else
            {
                var request = new OrchestratorRequest
                {
                    SyncJob = syncJob,
                    Exclusionary = message.ApplicationProperties.ContainsKey("Exclusionary") ? Convert.ToBoolean(message.ApplicationProperties["Exclusionary"]) : false,
                    CurrentPart = message.ApplicationProperties.ContainsKey("CurrentPart") ? Convert.ToInt32(message.ApplicationProperties["CurrentPart"]) : 0,
                    TotalParts = message.ApplicationProperties.ContainsKey("TotalParts") ? Convert.ToInt32(message.ApplicationProperties["TotalParts"]) : 0,
                    IsDestinationPart = message.ApplicationProperties.ContainsKey("IsDestinationPart") ? Convert.ToBoolean(message.ApplicationProperties["IsDestinationPart"]) : false,
                };

                var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), request);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceId: {instanceId} for job Id: {syncJob.Id} ", RunId = runId });
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }
    }
}