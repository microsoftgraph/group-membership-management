// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Repositories.Contracts;
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
            var mainRequest = context.GetInput<OrchestratorRequest>();
            if (mainRequest?.Message != null)
            {
                var message = mainRequest.Message;
                var runId = message.RunId;
                
                await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), message);
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { RunId = runId, Message = $"{nameof(OrchestratorFunction)} function completed", Verbosity = VerbosityLevel.DEBUG });
            }
        }
    }
}