// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Hosts.AgentReader.Services.Contracts;
using Hosts.AgentReader.Services.Entities;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader.Services
{
    public sealed class AgentReaderService : IAgentReaderService
    {
        private readonly IAgentGraphRepository _graphRepository;
        private readonly IAgentTableRepository _tableRepository;
        private readonly ILogger<AgentReaderService> _logger;

        public AgentReaderService(
            IAgentGraphRepository graphRepository,
            IAgentTableRepository tableRepository,
            ILogger<AgentReaderService> logger)
        {
            _graphRepository = graphRepository ?? throw new ArgumentNullException(nameof(graphRepository));
            _tableRepository = tableRepository ?? throw new ArgumentNullException(nameof(tableRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task ValidateTableAsync(Guid runId, CancellationToken cancellationToken) =>
            _tableRepository.ValidateTableAsync(runId, cancellationToken);

        public Task<int> WriteAsync(Guid runId, IReadOnlyList<AgentRecord> agents, CancellationToken cancellationToken) =>
            _tableRepository.WriteAsync(runId, agents, cancellationToken);

        public Task<long> GetRowCountAsync(Guid runId, CancellationToken cancellationToken) =>
            _tableRepository.GetRowCountAsync(runId, cancellationToken);

        public async Task<AgentPage> ReadPageAsync(Guid runId, string? nextLink, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(runId);
            var source = await _graphRepository.ReadPageAsync(runId, nextLink, cancellationToken);
            var agents = new List<AgentRecord>(source.Agents.Count);
            var counters = new AgentReadCounters { TotalEnumerated = source.Agents.Count };

            foreach (var agent in source.Agents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!agent.HasManager)
                {
                    counters.FilteredNoManager++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(agent.ManagerIdentifier))
                {
                    counters.FilteredManagerEmployeeIdAbsent++;
                    continue;
                }

                var managerIdentifier = agent.ManagerIdentifier.AsSpan().Trim();
                if (!IsIntegerText(managerIdentifier))
                {
                    counters.FilteredManagerEmployeeIdNonNumeric++;
                    continue;
                }

                if (!int.TryParse(managerIdentifier, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var managerId))
                {
                    counters.FilteredManagerEmployeeIdOutOfRange++;
                    continue;
                }

                if (!Guid.TryParse(agent.Id, out var agentId) || agentId == Guid.Empty)
                {
                    counters.FilteredInvalidAgentObjectId++;
                    continue;
                }

                var blueprintId = agent.BlueprintId;
                if (blueprintId?.Length > 200)
                {
                    blueprintId = blueprintId[..200];
                    counters.BlueprintIdTruncated++;
                }

                if (!agent.AccountEnabled.HasValue)
                {
                    counters.AccountEnabledDefaultApplied++;
                }

                agents.Add(new AgentRecord(agentId, managerId, blueprintId, agent.AccountEnabled ?? true));
                counters.FilterPassingAgents++;
            }

            _logger.PageProcessed(counters.TotalEnumerated, counters.FilterPassingAgents, counters.TotalFiltered);
            return new AgentPage(agents, source.NextLink, counters);
        }

        private static bool IsIntegerText(ReadOnlySpan<char> value)
        {
            if (!value.IsEmpty && (value[0] == '+' || value[0] == '-'))
            {
                value = value[1..];
            }

            if (value.IsEmpty)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (character < '0' || character > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
