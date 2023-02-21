// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public OrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function started" }, VerbosityLevel.DEBUG);

            var syncJobsDueToRun = new List<SyncJob>();

            var readertasks = new List<Task<List<SyncJob>>>{
                context.CallActivityAsync<List<SyncJob>>(nameof(SyncJobsReaderFunction), SyncStatus.Idle),
                context.CallActivityAsync<List<SyncJob>>(nameof(SyncJobsReaderFunction), SyncStatus.InProgress),
                context.CallActivityAsync<List<SyncJob>>(nameof(SyncJobsReaderFunction), SyncStatus.StuckInProgress)
            };

            var results = await Task.WhenAll(readertasks);
            syncJobsDueToRun.AddRange(results.SelectMany(x => x));

            if (syncJobsDueToRun != null && syncJobsDueToRun.Count > 0)
            {
                // Run multiple sync job processing flows in parallel
                var processingTasks = new List<Task>();
                foreach (var syncJob in syncJobsDueToRun)
                {
                    syncJob.RunId = context.NewGuid();
                    _loggingRepository.SetSyncJobProperties(syncJob.RunId.Value, syncJob.ToDictionary());
                    var processTask = context.CallSubOrchestratorAsync(nameof(SubOrchestratorFunction), syncJob);
                    processingTasks.Add(processTask);
                }

                await Task.WhenAll(processingTasks);
            }

            syncJobsDueToRun.ForEach(x =>
            {
                if (x.RunId.HasValue)
                    _loggingRepository.RemoveSyncJobProperties(x.RunId.Value);
            });

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}