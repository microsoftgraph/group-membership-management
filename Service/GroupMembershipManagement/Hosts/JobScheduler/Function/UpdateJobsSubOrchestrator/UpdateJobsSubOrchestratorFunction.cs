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

            await context.CallActivityAsync(nameof(BatchUpdateJobsFunction),
                        new BatchUpdateJobsRequest
                        {
                            SyncJobBatch = jobsToUpdate
                        });

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"Updating {jobsToUpdate.Count} total jobs..."
                });

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"Updated {jobsToUpdate.Count} total jobs."
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