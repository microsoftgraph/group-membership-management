// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class OrchestratorFunction
    {
        public OrchestratorFunction(INonProdService nonProdService)
        {
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var runId = context.NewGuid();

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function started", RunId = runId, Verbosity = VerbosityLevel.DEBUG });

            await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(
                nameof(IntegrationTestingPrepSubOrchestratorFunction),
                runId
                );

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function completed", RunId = runId, Verbosity = VerbosityLevel.DEBUG });
        }
    }
}
