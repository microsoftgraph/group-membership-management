// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Hosts.JobScheduler
{
    public class UpdateJobsSubOrchestratorFunction
    {
        public UpdateJobsSubOrchestratorFunction()
        {
        }

        [FunctionName(nameof(UpdateJobsSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            await context.CallActivityAsync(nameof(LoggerFunction),
                                                      new LoggerRequest
                                                      {
                                                          Message = $"{nameof(UpdateJobsSubOrchestratorFunction)} function started",
                                                          Verbosity = VerbosityLevel.DEBUG
                                                      });

            var request = context.GetInput<UpdateJobsSubOrchestratorRequest>();
            var jobsToUpdate = request.JobsToUpdate;

            var BATCH_SIZE = 100;
            var groupingsByPartitionKey = jobsToUpdate.GroupBy(x => x.Id);

            var batchTasks = new List<Task>();

            foreach (var grouping in groupingsByPartitionKey)
            {
                var jobsBatches = grouping.Select((x, idx) => new { x, idx })
                .GroupBy(x => x.idx / BATCH_SIZE)
                .Select(g => g.Select(a => a.x));

                foreach (var batch in jobsBatches)
                {
                    batchTasks.Add(context.CallActivityAsync(nameof(BatchUpdateJobsFunction),
                        new BatchUpdateJobsRequest
                        {
                            SyncJobBatch = batch
                        }));
                }
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"Updating {jobsToUpdate.Count} total jobs in {batchTasks.Count} batches of 100 jobs..."
                });

            await Task.WhenAll(batchTasks);

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"Updated {jobsToUpdate.Count} total jobs in {batchTasks.Count} batches of 100 jobs."
                });

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                      new LoggerRequest
                                                      {
                                                          Message = $"{nameof(UpdateJobsSubOrchestratorFunction)} function completed",
                                                          Verbosity = VerbosityLevel.DEBUG
                                                      });
        }
    }
}