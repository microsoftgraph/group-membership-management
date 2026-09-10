// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AgentReader.Services.Contracts;
using Hosts.AgentReader.Services.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader
{
    public sealed class AgentReaderFunction
    {
        private readonly IAgentReaderService _service;
        private readonly ILogger<AgentReaderFunction> _logger;

        public AgentReaderFunction(IAgentReaderService service, ILogger<AgentReaderFunction> logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(AgentReaderFunction))]
        public async Task<AgentPage> RunAsync([ActivityTrigger] AgentPageRequest request, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(request.RunId);
            _logger.FunctionStarted(nameof(AgentReaderFunction));
            var page = await _service.ReadPageAsync(request.RunId, request.NextLink, cancellationToken);
            _logger.FunctionCompleted(nameof(AgentReaderFunction));
            return page;
        }
    }
}
