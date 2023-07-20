// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
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

            var runId = context.NewGuid();

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });

            var syncJobs = new List<SyncJob>();
            var segmentResponse = await context.CallActivityAsync<List<SyncJob>>(nameof(GetJobsSegmentedFunction), null);

            syncJobs = segmentResponse;

            await context.CallActivityAsync(nameof(LoggerFunction),
             new LoggerRequest
             {
                 RunId = runId,
                 Message = $"{nameof(OrchestratorFunction)} number of jobs in the syncJobs List: {syncJobs.Count}",
                 Verbosity = VerbosityLevel.DEBUG
             });

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

            await context.CallActivityAsync(nameof(LoggerFunction),
               new LoggerRequest
               {
                   RunId = runId,
                   Message = $"{nameof(OrchestratorFunction)} function completed at: {context.CurrentUtcDateTime}",
                   Verbosity = VerbosityLevel.DEBUG
               });

        }
    }
}