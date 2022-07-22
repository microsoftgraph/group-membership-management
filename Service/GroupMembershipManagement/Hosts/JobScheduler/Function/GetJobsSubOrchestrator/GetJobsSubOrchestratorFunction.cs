// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Azure.Cosmos.Table;
using Services.Entities;
using Newtonsoft.Json;
using Azure;

namespace Hosts.JobScheduler
{
    public class GetJobsSubOrchestratorFunction
    {
        private List<SyncJob> _jobs;

        public GetJobsSubOrchestratorFunction()
        {
            _jobs = new List<SyncJob>();
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

            AsyncPageable<SyncJob> pageableQueryResult = null;
            string continuationToken = null;

            do
            {
                var segmentResponse = await context.CallActivityAsync<GetJobsSegmentedResponse>(nameof(GetJobsSegmentedFunction),
                    new GetJobsSegmentedRequest
                    {
                        PageableQueryResult = pageableQueryResult,
                        ContinuationToken = continuationToken
                    });

                _jobs.AddRange(segmentResponse.JobsSegment);

                pageableQueryResult = segmentResponse.PageableQueryResult;
                continuationToken = segmentResponse.ContinuationToken;

            } while (continuationToken != null);

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                      new LoggerRequest
                                                      {
                                                          Message = $"{nameof(GetJobsSubOrchestratorFunction)} function completed",
                                                          Verbosity = VerbosityLevel.DEBUG
                                                      });

            var schedulerSyncJobs = new List<SchedulerSyncJob>();
            foreach (var job in _jobs)
            {
                var serializedJob = JsonConvert.SerializeObject(job);
                SchedulerSyncJob schedulerJob = JsonConvert.DeserializeObject<SchedulerSyncJob>(serializedJob);
                schedulerSyncJobs.Add(schedulerJob);
            }

            return schedulerSyncJobs;
        }
    }
}