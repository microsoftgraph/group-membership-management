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
        public async Task RunOrchestrator(
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

            var jobsToUpdate = await context.CallActivityAsync<List<SchedulerSyncJob>>(nameof(GetJobsToUpdateFunction), null);

            if (_jobSchedulerConfig.ResetJobs)
            {
                await context.CallActivityAsync(nameof(ResetJobsFunction),
                    new ResetJobsRequest
                    {
                        JobsToReset = jobsToUpdate
                    });
            }

            if (_jobSchedulerConfig.DistributeJobs)
            {
                await context.CallActivityAsync(nameof(DistributeJobsFunction),
                    new DistributeJobsRequest
                    {
                        JobsToDistribute = jobsToUpdate
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
