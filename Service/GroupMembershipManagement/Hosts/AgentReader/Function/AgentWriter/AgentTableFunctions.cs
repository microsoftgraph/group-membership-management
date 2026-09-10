// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AgentReader.Services.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader
{
    public sealed class AgentTableFunctions
    {
        private readonly IAgentReaderService _service;
        private readonly ILogger<AgentTableFunctions> _logger;

        public AgentTableFunctions(IAgentReaderService service, ILogger<AgentTableFunctions> logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(ValidateAgentTable))]
        public async Task ValidateAgentTable([ActivityTrigger] AgentReaderRequest request, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(request.RunId);
            _logger.FunctionStarted(nameof(ValidateAgentTable));
            await _service.ValidateTableAsync(request.RunId, cancellationToken);
            _logger.FunctionCompleted(nameof(ValidateAgentTable));
        }

        [Function(nameof(GetAgentRowCount))]
        public async Task<long> GetAgentRowCount([ActivityTrigger] AgentReaderRequest request, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(request.RunId);
            _logger.FunctionStarted(nameof(GetAgentRowCount));
            var count = await _service.GetRowCountAsync(request.RunId, cancellationToken);
            _logger.FunctionCompleted(nameof(GetAgentRowCount));
            return count;
        }
    }
}
