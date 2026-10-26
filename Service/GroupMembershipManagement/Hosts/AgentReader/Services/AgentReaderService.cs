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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader.Services
{
    public sealed class AgentReaderService : IAgentReaderService
    {
        private readonly IAgentGraphRepository _graphRepository;
        private readonly IAgentTableRepository _tableRepository;
        private readonly ILogger<AgentReaderService> _logger;
        private const int MaxBlueprintNameLength = 256;
        private const string BlueprintMappingColumnName = "Blueprint";

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

        // Names are a convenience for the UI: anything Graph cannot resolve is described by its code, and a failed
        // lookup never fails the run. Only the SQL write is required to succeed.
        public async Task<int> WriteBlueprintMappingsAsync(Guid runId, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(runId);
            var codes = await _tableRepository.GetBlueprintCodesAsync(runId, cancellationToken);
            var descriptions = codes.ToDictionary(code => code, code => code, StringComparer.Ordinal);
            var guids = codes
                .Select(code => Guid.TryParseExact(code, "D", out var id) && id != Guid.Empty ? id : (Guid?)null)
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

            var resolved = 0;
            if (guids.Count > 0)
            {
                try
                {
                    var names = await _graphRepository.GetBlueprintNamesAsync(runId, guids, cancellationToken);
                    foreach (var code in codes)
                    {
                        if (Guid.TryParseExact(code, "D", out var id) && names.TryGetValue(id, out var name))
                        {
                            var trimmed = name.Trim();
                            if (trimmed.Length > 0)
                            {
                                descriptions[code] = trimmed.Length > MaxBlueprintNameLength ? trimmed[..MaxBlueprintNameLength] : trimmed;
                                resolved++;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.BlueprintNameLookupFailed(exception, guids.Count);
                }
            }

            await _tableRepository.WriteMappingsAsync(runId, BlueprintMappingColumnName, descriptions, cancellationToken);
            _logger.BlueprintMappingsWritten(descriptions.Count, resolved);
            return descriptions.Count;
        }

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
