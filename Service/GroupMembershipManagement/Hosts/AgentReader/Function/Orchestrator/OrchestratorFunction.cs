// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AgentReader.Services.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.AgentReader
{
    public sealed class OrchestratorFunction
    {
        [Function(nameof(OrchestratorFunction))]
        public async Task<AgentReaderResult> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<AgentReaderRequest>()
                ?? throw new InvalidOperationException("AgentReader requires a RunId.");
            if (request.RunId == Guid.Empty)
            {
                throw new InvalidOperationException("AgentReader requires a non-empty RunId.");
            }

            var logger = context.CreateReplaySafeLogger("Hosts.AgentReader.OrchestratorFunction");
            using var scope = logger.BeginRunIdScope(request.RunId);
            var counters = new AgentReadCounters();
            var status = "Failed";
            logger.PopulationStarted();

            try
            {
                await context.CallActivityAsync(nameof(AgentTableFunctions.ValidateAgentTable), request);
                var continuationLinks = new HashSet<string>(StringComparer.Ordinal);
                string? nextLink = null;

                do
                {
                    var page = await context.CallActivityAsync<AgentPage>(
                        nameof(AgentReaderFunction), new AgentPageRequest(request.RunId, nextLink));
                    counters.Add(page.Counters);

                    if (page.Counters.Inserted != 0
                        || page.Counters.FilterPassingAgents != page.Agents.Count
                        || page.Counters.TotalEnumerated != page.Counters.FilterPassingAgents + page.Counters.TotalFiltered)
                    {
                        throw new InvalidDataException("Agent page counters do not reconcile.");
                    }

                    if (page.Agents.Count > 0)
                    {
                        var written = await context.CallActivityAsync<int>(
                            nameof(AgentWriterFunction), new AgentWriteRequest(request.RunId, page.Agents));
                        counters.Inserted = checked(counters.Inserted + written);
                        if (written != page.Agents.Count)
                        {
                            throw new InvalidDataException("Not all eligible agents were persisted.");
                        }
                    }

                    nextLink = page.NextLink;
                    if (nextLink != null && (string.IsNullOrWhiteSpace(nextLink) || !continuationLinks.Add(nextLink)))
                    {
                        throw new InvalidDataException("Graph returned an invalid or repeated continuation link.");
                    }
                }
                while (nextLink != null);

                var persisted = await context.CallActivityAsync<long>(nameof(AgentTableFunctions.GetAgentRowCount), request);
                if (persisted != counters.Inserted
                    || counters.Inserted != counters.FilterPassingAgents
                    || counters.TotalEnumerated != checked(counters.Inserted + counters.TotalFiltered))
                {
                    throw new InvalidDataException("Persisted agent rows do not reconcile with the complete enumeration.");
                }

                status = counters.FilterPassingAgents == 0 ? "CompletedEmpty" : "Completed";
                return new AgentReaderResult(request.RunId, status, counters);
            }
            finally
            {
                logger.PopulationCounters(
                    status, counters.TotalEnumerated, counters.Inserted, counters.FilterPassingAgents,
                    counters.FilteredNoManager, counters.FilteredManagerEmployeeIdAbsent,
                    counters.FilteredManagerEmployeeIdNonNumeric, counters.FilteredManagerEmployeeIdOutOfRange,
                    counters.FilteredInvalidAgentObjectId, counters.AccountEnabledDefaultApplied, counters.BlueprintIdTruncated);
                if (status == "Failed")
                {
                    logger.PopulationFailed();
                }
            }
        }
    }
}
