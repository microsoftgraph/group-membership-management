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
        private readonly IGraphGroupRepository _graphGroupRepository = null;
        public OrchestratorFunction(ILoggingRepository loggingRepository,
                                    IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function started" });
            var syncJobs = await context.CallActivityAsync<List<SyncJob>>(nameof(SyncJobsReaderFunction), null);
            if (syncJobs != null && syncJobs.Count > 0)
            {
                // Run multiple sync job processing flows in parallel
                var processingTasks = new List<Task>();
                foreach (var syncJob in syncJobs)
                {
                    syncJob.RunId = _graphGroupRepository.RunId = context.NewGuid();
                    _loggingRepository.SyncJobProperties = syncJob.ToDictionary();
                    var processTask = context.CallSubOrchestratorAsync(nameof(SubOrchestratorFunction), syncJob);
                    processingTasks.Add(processTask);
                }
                await Task.WhenAll(processingTasks);
            }
            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed" });
        }
    }
}