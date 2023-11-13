// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
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

        [FunctionName(nameof(GetJobsSubOrchestratorFunction))]
        public async Task<List<DistributionSyncJob>> RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"{nameof(GetJobsSubOrchestratorFunction)} function started",
                    Verbosity = VerbosityLevel.DEBUG
                });

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = "Retrieving enabled sync jobs" + (_jobSchedulerConfig.IncludeFutureJobs ? " including those with future StartDate values" : "")
                });

            var jobs = new List<DistributionSyncJob>();

            var segmentResponse = await context.CallActivityAsync<GetJobsResponse>(nameof(GetJobsFunction), new GetJobsRequest
            {
                IncludeFutureJobs = _jobSchedulerConfig.IncludeFutureJobs
            });

            jobs = segmentResponse.JobsSegment;

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"Retrieved {jobs.Count} enabled sync jobs" + (_jobSchedulerConfig.IncludeFutureJobs ? " including those with future StartDate values" : "")
                });

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"{nameof(GetJobsSubOrchestratorFunction)} function completed",
                    Verbosity = VerbosityLevel.DEBUG
                });

            return jobs;
        }
    }
}