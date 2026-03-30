// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Repositories.Contracts.Helpers;
using System.Threading.Tasks;

namespace Hosts.SyncJobUpdater
{
    public class OrchestratorFunction
    {
        public OrchestratorFunction()
        {
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger(nameof(OrchestratorFunction));
            var mainRequest = context.GetInput<OrchestratorRequest>();
            
            if (mainRequest?.Message != null)
            {
                var message = mainRequest.Message;
                
                using (logger.BeginSyncJobScope(message.SyncJob))
                {
                    logger.FunctionStartedAt(nameof(OrchestratorFunction), context.CurrentUtcDateTime);

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), message);
                    
                    logger.FunctionCompleted(nameof(OrchestratorFunction));
                }
            }
        }
    }
}