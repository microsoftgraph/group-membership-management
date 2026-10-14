// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Services;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class DemoAgentSubOrchestratorFunction
    {
        public const int BatchSize = 20;

        [Function(nameof(DemoAgentSubOrchestratorFunction))]
        public async Task<DemoAgentWriterResult> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("AzureUserReader.DemoAgentSubOrchestratorFunction");
            var request = context.GetInput<DemoAgentRequest>();
            var total = new DemoAgentWriterResult();

            var plan = DemoAgentPlanner.Plan(request.Managers, request.AgentCount, request.MaxAgentsPerUser, request.TenantDomain);
            if (plan.Capacity < plan.Requested)
                logger.DemoAgentCapacityShortfall(plan.Requested, plan.Capacity);

            var existing = await context.CallActivityAsync<List<ExistingDemoAgent>>(nameof(DemoAgentReaderFunction), request.TenantDomain);
            var reconciliation = DemoAgentPlanner.Reconcile(plan, existing);

            if (reconciliation.ToCreate.Count > 0 || reconciliation.ToFixManager.Count > 0)
            {
                var blueprintAppId = reconciliation.ToCreate.Count > 0
                    ? await context.CallActivityAsync<string>(nameof(DemoAgentBlueprintFunction),
                        new DemoAgentWriterRequest { SponsorId = reconciliation.ToCreate[0].ManagerId })
                    : null;

                var batches = reconciliation.ToCreate.Chunk(BatchSize)
                    .Select(c => new DemoAgentWriterRequest { BlueprintAppId = blueprintAppId, ToCreate = c.ToList() })
                    .Concat(reconciliation.ToFixManager.Chunk(BatchSize)
                        .Select(c => new DemoAgentWriterRequest { ToFixManager = c.ToList() }));

                foreach (var batch in batches)
                {
                    var result = await context.CallActivityAsync<DemoAgentWriterResult>(nameof(DemoAgentWriterFunction), batch);
                    total.Created += result.Created;
                    total.ManagersFixed += result.ManagersFixed;
                    total.ManagerFailures += result.ManagerFailures;
                    total.Failed += result.Failed;
                }
            }

            logger.DemoAgentSummary(plan.Requested, plan.Capacity, plan.Agents.Count, reconciliation.AlreadyPresent,
                total.Created, total.ManagersFixed, total.ManagerFailures, total.Failed);

            if (reconciliation.ToCreate.Count > 0 && total.Created == 0)
                throw new InvalidOperationException($"All {reconciliation.ToCreate.Count} demo agent creations failed.");

            return total;
        }
    }
}
