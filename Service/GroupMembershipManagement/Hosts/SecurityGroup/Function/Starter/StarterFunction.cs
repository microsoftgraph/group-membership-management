// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _log;
        private readonly ISyncJobRepository _syncJob;
        private readonly bool _isSecurityGroupDryRunEnabled;

        public StarterFunction(ILoggingRepository loggingRepository, ISyncJobRepository syncJob, IDryRunValue dryRun)
        {
            _log = loggingRepository;
            _syncJob = syncJob;
            _isSecurityGroupDryRunEnabled = dryRun.DryRunEnabled;
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task Run([ServiceBusTrigger("%serviceBusSyncJobTopic%", "SecurityGroup", Connection = "serviceBusTopicConnection")] Message message,
                              [DurableClient] IDurableOrchestrationClient starter,
                              ILogger log)
        {
            var syncJob = JsonConvert.DeserializeObject<SyncJob>(Encoding.UTF8.GetString(message.Body));
            _log.SyncJobProperties = syncJob.ToDictionary();

            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = syncJob.RunId });

            if ((DateTime.UtcNow - syncJob.DryRunTimeStamp) < TimeSpan.FromHours(syncJob.Period) && _isSecurityGroupDryRunEnabled == true)
            {
                await _syncJob.UpdateSyncJobStatusAsync(new[] { syncJob }, SyncStatus.Idle);
                await _log.LogMessageAsync(new LogMessage { Message = $"Setting the status of the sync back to Idle as the sync has run within the previous DryRunTimeStamp period", RunId = syncJob.RunId });
            }
            else
                await starter.StartNewAsync(nameof(OrchestratorFunction), syncJob);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = syncJob.RunId });
        }
    }
}