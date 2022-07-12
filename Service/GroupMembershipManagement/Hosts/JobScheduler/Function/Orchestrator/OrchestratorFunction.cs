// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class OrchestratorFunction
    {
        private readonly IJobSchedulerConfig _jobSchedulerConfig;

        public OrchestratorFunction(IJobSchedulerConfig jobSchedulerConfig)
        {
            _jobSchedulerConfig = jobSchedulerConfig ?? throw new ArgumentNullException(nameof(jobSchedulerConfig));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var runId = context.NewGuid();

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });

            var jobsToUpdate = await context.CallSubOrchestratorAsync<List<SchedulerSyncJob>>(nameof(GetJobsSubOrchestratorFunction), null);

            List<SchedulerSyncJob> jobsWithUpdates = null;

            if (_jobSchedulerConfig.ResetJobs)
            {
                jobsWithUpdates = await context.CallActivityAsync<List<SchedulerSyncJob>>(nameof(ResetJobsFunction),
                    new ResetJobsRequest
                    {
                        JobsToReset = jobsToUpdate
                    });

                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        RunId = runId,
                        Message = $"Successfully reset jobs to update."
                    });
            }

            else if (_jobSchedulerConfig.DistributeJobs)
            {
                jobsWithUpdates = await context.CallActivityAsync<List<SchedulerSyncJob>>(nameof(DistributeJobsFunction),
                    new DistributeJobsRequest
                    {
                        JobsToDistribute = jobsToUpdate
                    });

                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        RunId = runId,
                        Message = $"Successfully distributed jobs to update."
                    });
            }

            if(jobsWithUpdates != null)
            {
                await context.CallSubOrchestratorAsync(nameof(UpdateJobsSubOrchestratorFunction), 
                    new UpdateJobsSubOrchestratorRequest
                    {
                        JobsToUpdate = jobsWithUpdates
                    });

                await context.CallActivityAsync(nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        RunId = runId,
                        Message = $"Successfully updated all jobs accordingly."
                    });
            }

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
