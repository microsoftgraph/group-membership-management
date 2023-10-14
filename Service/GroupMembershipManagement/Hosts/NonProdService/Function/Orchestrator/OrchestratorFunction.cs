// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System;
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

            var tenantUserCount = await context.CallActivityAsync<int?>(
                nameof(TenantUserCountFunction),
                new TenantUserReaderRequest
                {
                    RunId = runId
                });

            if (tenantUserCount == null)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Error with {nameof(TenantUserCountFunction)}, check exception" });
                throw new Exception($"Error occurred in the {nameof(TenantUserCountFunction)}, when attempting to get a count of the number of users in the tenant.");
            }

            await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(
                nameof(IntegrationTestingPrepSubOrchestratorFunction),
                new IntegrationTestingPrepSubOrchestratorRequest {
                    RunId = runId,
                    TenantUserCount = tenantUserCount.Value
                });

            await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(
                nameof(LoadTestingPrepSubOrchestratorFunction),
                new LoadTestingPrepSubOrchestratorRequest
                {
                    RunId = runId,
                    TenantUserCount = tenantUserCount.Value
                }
                );

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function completed", RunId = runId, Verbosity = VerbosityLevel.DEBUG });
        }
    }
}
