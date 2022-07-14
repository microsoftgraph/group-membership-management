// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
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
            var syncJobs = await context.CallActivityAsync<List<SyncJob>>(nameof(SyncJobsReaderFunction), null);
            if (syncJobs != null && syncJobs.Count > 0)
            {
                // Run multiple sync job processing flows in parallel
                var processingTasks = new List<Task>();
                foreach (var syncJob in syncJobs)
                {
                    syncJob.RunId = context.NewGuid();
                    _loggingRepository.SetSyncJobProperties(syncJob.RunId.Value, syncJob.ToDictionary());
                    var processTask = context.CallSubOrchestratorAsync(nameof(SubOrchestratorFunction), syncJob);
                    processingTasks.Add(processTask);
                }

                await Task.WhenAll(processingTasks);
            }

            syncJobs.ForEach(x =>
            {
                if (x.RunId.HasValue)
                    _loggingRepository.RemoveSyncJobProperties(x.RunId.Value);
            });

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}