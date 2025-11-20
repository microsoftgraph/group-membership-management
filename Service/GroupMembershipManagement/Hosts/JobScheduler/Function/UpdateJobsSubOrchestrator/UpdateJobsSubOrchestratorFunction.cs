// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class UpdateJobsSubOrchestratorFunction
    {
        public UpdateJobsSubOrchestratorFunction()
        {
        }

        [Function(nameof(UpdateJobsSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
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