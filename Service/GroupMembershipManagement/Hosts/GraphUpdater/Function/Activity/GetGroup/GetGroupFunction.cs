// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GetGroupFunction
    {
        private readonly ILogger<GetGroupFunction> _logger;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public GetGroupFunction(ILogger<GetGroupFunction> logger, IGraphUpdaterService graphUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [Function(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupNameAsync([ActivityTrigger] GetGroupRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(GetGroupFunction));
            var groupId = await _graphUpdaterService.GetGroupIdAsync(request.SyncJob);
            _logger.FunctionCompleted(nameof(GetGroupFunction));
            return groupId;
        }
    }
}