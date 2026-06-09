// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.InjectConfig;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class GetJobsSubOrchestratorFunction
    {
        private readonly IJobSchedulerConfig _jobSchedulerConfig;

        public GetJobsSubOrchestratorFunction(IJobSchedulerConfig jobSchedulerConfig)
        {
            _jobSchedulerConfig = jobSchedulerConfig;
        }

        [Function(nameof(GetJobsSubOrchestratorFunction))]
        public async Task<List<DistributionSyncJob>> RunSubOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger($"JobScheduler.{nameof(GetJobsSubOrchestratorFunction)}");

            logger.FunctionStarted(nameof(GetJobsSubOrchestratorFunction));

            logger.RetrievingEnabledSyncJobs();

            var jobs = new List<DistributionSyncJob>();

            var segmentResponse = await context.CallActivityAsync<GetJobsResponse>(nameof(GetJobsFunction), null);

            jobs = segmentResponse.JobsSegment;

            logger.RetrievedEnabledSyncJobs(jobs.Count);

            logger.FunctionCompleted(nameof(GetJobsSubOrchestratorFunction));

            return jobs;
        }
    }
}
