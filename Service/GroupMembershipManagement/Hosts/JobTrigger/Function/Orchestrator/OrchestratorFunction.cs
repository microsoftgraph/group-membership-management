// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class OrchestratorFunction
    {
        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger(nameof(OrchestratorFunction));
            var runId = context.NewGuid();

            using (logger.BeginRunIdScope(runId))
            {
                logger.FunctionStarted(nameof(OrchestratorFunction));

                var syncJobs = await context.CallActivityAsync<List<SyncJob>>(nameof(GetJobsFunction), (object)null);
                logger.OrchestratorJobCount(nameof(OrchestratorFunction), syncJobs.Count);

                if (syncJobs != null && syncJobs.Count > 0)
                {
                    // Run multiple sync job processing flows in parallel
                    var processingTasks = new List<Task>();
                    foreach (var syncJob in syncJobs)
                    {
                        syncJob.RunId = context.NewGuid();
                        var processTask = context.CallSubOrchestratorAsync(nameof(SubOrchestratorFunction), syncJob);
                        processingTasks.Add(processTask);
                    }

                    await Task.WhenAll(processingTasks);
                }

                logger.FunctionCompleted(nameof(OrchestratorFunction));
            }
        }
    }
}
