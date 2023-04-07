// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class GetJobsSubOrchestratorFunction
    {
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly IJobSchedulerConfig _jobSchedulerConfig;

        public GetJobsSubOrchestratorFunction(ISyncJobRepository syncJobRepository, IJobSchedulerConfig jobSchedulerConfig)
        {
            _syncJobRepository = syncJobRepository;
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

            string query = null;
            string continuationToken = null;
            var jobs = new List<DistributionSyncJob>();

            do
            {
                var segmentResponse = await context.CallActivityAsync<GetJobsSegmentedResponse>(nameof(GetJobsSegmentedFunction),
                    new GetJobsSegmentedRequest
                    {
                        Query = query,
                        ContinuationToken = continuationToken,
                        IncludeFutureJobs = _jobSchedulerConfig.IncludeFutureJobs
                    });

                jobs.AddRange(segmentResponse.JobsSegment);
                query = segmentResponse.Query;
                continuationToken = segmentResponse.ContinuationToken;

            } while (continuationToken != null);

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