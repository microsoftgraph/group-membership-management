// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
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
            var logger = context.CreateReplaySafeLogger($"JobScheduler.{nameof(UpdateJobsSubOrchestratorFunction)}");

            logger.FunctionStarted(nameof(UpdateJobsSubOrchestratorFunction));

            var request = context.GetInput<UpdateJobsSubOrchestratorRequest>();
            var jobsToUpdate = request.JobsToUpdate;

            await context.CallActivityAsync(nameof(BatchUpdateJobsFunction),
                        new BatchUpdateJobsRequest
                        {
                            SyncJobBatch = jobsToUpdate
                        });

            logger.UpdatingTotalJobs(jobsToUpdate.Count);

            logger.UpdatedTotalJobs(jobsToUpdate.Count);

            logger.FunctionCompleted(nameof(UpdateJobsSubOrchestratorFunction));
        }
    }
}
