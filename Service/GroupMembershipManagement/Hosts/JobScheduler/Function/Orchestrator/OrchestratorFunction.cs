// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
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

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("JobScheduler.OrchestratorFunction");
            var runId = context.NewGuid();

            using var scope = logger.BeginRunIdScope(runId);

            logger.OrchestratorStarted(nameof(OrchestratorFunction), context.CurrentUtcDateTime);

            var orchestratorRequest = context.GetInput<OrchestratorRequest>();
            var prioritizeThresholdJobs = false;
            if(orchestratorRequest != null)
            {
                _jobSchedulerConfig.StartTimeDelayMinutes = orchestratorRequest.StartTimeDelayMinutes;
                prioritizeThresholdJobs = orchestratorRequest.PrioritizeThresholdJobs;
            }

            if(!_jobSchedulerConfig.ResetJobs && !_jobSchedulerConfig.DistributeJobs)
            {
                logger.OrchestratorCompletedImmediately(nameof(OrchestratorFunction), context.CurrentUtcDateTime);
                return;
            }

            var jobsToUpdate = await context.CallSubOrchestratorAsync<List<DistributionSyncJob>>(nameof(GetJobsSubOrchestratorFunction), null);

            List<DistributionSyncJob> jobsWithUpdates = null;

            if (_jobSchedulerConfig.ResetJobs)
            {
                jobsWithUpdates = await context.CallActivityAsync<List<DistributionSyncJob>>(nameof(ResetJobsFunction),
                    new ResetJobsRequest
                    {
                        JobsToReset = jobsToUpdate,
                        DaysToAddForReset = _jobSchedulerConfig.DaysToAddForReset
                    });

                logger.JobsResetSuccessfully();
            }

            else if (_jobSchedulerConfig.DistributeJobs)
            {
                jobsWithUpdates = await context.CallActivityAsync<List<DistributionSyncJob>>(nameof(DistributeJobsFunction),
                    new DistributeJobsRequest
                    {
                        JobsToDistribute = jobsToUpdate,
                        StartTimeDelayMinutes = _jobSchedulerConfig.StartTimeDelayMinutes,
                        DelayBetweenSyncsSeconds = _jobSchedulerConfig.DelayBetweenSyncsSeconds,
                        PrioritizeThresholdJobs = prioritizeThresholdJobs
                    });

                logger.JobsDistributedSuccessfully();
            }

            if (jobsWithUpdates != null && jobsWithUpdates.Count > 0)
            {
                await context.CallSubOrchestratorAsync(nameof(UpdateJobsSubOrchestratorFunction),
                    new UpdateJobsSubOrchestratorRequest
                    {
                        JobsToUpdate = jobsWithUpdates
                    });

                logger.JobsUpdatedSuccessfully();
            }

            logger.OrchestratorCompleted(nameof(OrchestratorFunction), context.CurrentUtcDateTime);
        }
    }
}
