// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Entities;
using Azure;
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
        public async Task<List<SchedulerSyncJob>> RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
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

            AsyncPageable<SyncJob> pageableQueryResult = null;
            string continuationToken = null;
            var jobs = new List<SyncJob>();

            do
            {
                var segmentResponse = await context.CallActivityAsync<GetJobsSegmentedResponse>(nameof(GetJobsSegmentedFunction),
                    new GetJobsSegmentedRequest
                    {
                        PageableQueryResult = pageableQueryResult,
                        ContinuationToken = continuationToken
                    });

                jobs.AddRange(segmentResponse.JobsSegment);

                pageableQueryResult = segmentResponse.PageableQueryResult;
                continuationToken = segmentResponse.ContinuationToken;
            } while (continuationToken != null);

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"Retrieved {jobs.Count} enabled sync jobs" + (_jobSchedulerConfig.IncludeFutureJobs ? " including those with future StartDate values" : "")
                });

            var schedulerSyncJobs = new List<SchedulerSyncJob>();
            foreach (var job in jobs)
            {
                var serializedJob = JsonConvert.SerializeObject(job);
                SchedulerSyncJob schedulerJob = JsonConvert.DeserializeObject<SchedulerSyncJob>(serializedJob);
                schedulerSyncJobs.Add(schedulerJob);
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"{nameof(GetJobsSubOrchestratorFunction)} function completed",
                    Verbosity = VerbosityLevel.DEBUG
                });

            return schedulerSyncJobs;
        }
    }
}