// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
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

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var runId = context.NewGuid();
            var logger = context.CreateReplaySafeLogger("Hosts.NonProdService.OrchestratorFunction");
            using var scope = logger.BeginRunIdScope(runId);

            logger.FunctionStarted(nameof(OrchestratorFunction));

            var tenantUserCount = await context.CallActivityAsync<int?>(
                nameof(TenantUserCountFunction),
                new TenantUserCountRequest
                {
                    RunId = runId
                });

            if (tenantUserCount == null)
            {
                logger.ErrorWithFunction(nameof(TenantUserCountFunction));
                throw new Exception($"Error occurred in the {nameof(TenantUserCountFunction)}, when attempting to get a count of the number of users in the tenant.");
            }

            await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(
                nameof(IntegrationTestingPrepSubOrchestratorFunction),
                new IntegrationTestingPrepSubOrchestratorRequest
                {
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

            logger.FunctionCompleted(nameof(OrchestratorFunction));
        }
    }
}
