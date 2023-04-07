// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.Notifier
{
    public class OrchestratorFunction
    {
        public OrchestratorFunction()
        {
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var runId = context.NewGuid();

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });

            var request = context.GetInput<NotifierRequest>();
            var recipientAddresses = request.RecipientAddresses;

            await context.CallActivityAsync(nameof(SendNotificationFunction), recipientAddresses);

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    RunId = runId,
                    Message = $"{nameof(OrchestratorFunction)} function completed at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });
        }
    }
}