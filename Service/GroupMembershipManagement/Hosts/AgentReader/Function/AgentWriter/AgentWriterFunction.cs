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
    public sealed class AgentWriterFunction
    {
        private readonly IAgentReaderService _service;
        private readonly ILogger<AgentWriterFunction> _logger;

        public AgentWriterFunction(IAgentReaderService service, ILogger<AgentWriterFunction> logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(AgentWriterFunction))]
        public async Task<int> RunAsync([ActivityTrigger] AgentWriteRequest request, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(request.RunId);
            _logger.FunctionStarted(nameof(AgentWriterFunction));
            var written = await _service.WriteAsync(request.RunId, request.Agents, cancellationToken);
            _logger.FunctionCompleted(nameof(AgentWriterFunction));
            return written;
        }
    }
}
