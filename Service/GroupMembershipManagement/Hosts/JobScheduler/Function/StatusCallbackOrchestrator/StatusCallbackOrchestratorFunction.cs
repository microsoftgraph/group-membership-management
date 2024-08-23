// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Repositories.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.JobScheduler
{
    public class StatusCallbackOrchestratorFunction
    {
        private int INITIAL_DELAY_SECONDS = 20;
        private int WAIT_TIME_BETWEEN_STATUSCHECK_MINUTES = 1;

        public StatusCallbackOrchestratorFunction() 
        { 
        }

        [Function(nameof(StatusCallbackOrchestratorFunction))]
        public async Task RunStatusCallbackOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"{nameof(StatusCallbackOrchestratorFunction)} function started",
                    Verbosity = VerbosityLevel.DEBUG
                });

            var request = context.GetInput<StatusCallbackOrchestratorRequest>();

            var statusUrl = request.JobSchedulerStatusUrl;
            var statusRequest = new CheckJobSchedulerStatusRequest
            {
                StatusUrl = statusUrl
            };

            
            await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(INITIAL_DELAY_SECONDS), CancellationToken.None);

            var jobSchedulerCompleted = await context.CallActivityAsync<bool>(nameof(CheckJobSchedulerStatusFunction), statusRequest);   

            while (!jobSchedulerCompleted)
            {
                DateTime dueTime = context.CurrentUtcDateTime.AddMinutes(WAIT_TIME_BETWEEN_STATUSCHECK_MINUTES);
                await context.CreateTimer(dueTime, CancellationToken.None);

                jobSchedulerCompleted = await context.CallActivityAsync<bool>(nameof(CheckJobSchedulerStatusFunction), statusRequest);
            }

            await context.CallActivityAsync(nameof(PostCallbackFunction), new PostCallbackRequest
                {
                    AuthToken = request.AuthToken,
                    SuccessBody = request.SuccessBody,
                    CallbackUrl = request.CallbackUrl
                });

            await context.CallActivityAsync(nameof(LoggerFunction),
                            new LoggerRequest
                            {
                                Message = $"{nameof(StatusCallbackOrchestratorFunction)} function completed",
                                Verbosity = VerbosityLevel.DEBUG
                            });
        }
    }
}
