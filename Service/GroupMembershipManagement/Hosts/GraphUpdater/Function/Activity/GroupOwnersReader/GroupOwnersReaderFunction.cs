// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GroupOwnersReaderFunction
    {
        private readonly ILogger<GroupOwnersReaderFunction> _logger;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public GroupOwnersReaderFunction(ILogger<GroupOwnersReaderFunction> logger, IGraphUpdaterService graphUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [Function(nameof(GroupOwnersReaderFunction))]
        public async Task<List<AzureADUser>> GetGroupOwnersAsync([ActivityTrigger] GroupOwnersReaderRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(GroupOwnersReaderFunction));
            var owners = await _graphUpdaterService.GetGroupOwnersAsync(request.GroupId);
            _logger.FunctionCompleted(nameof(GroupOwnersReaderFunction));

            return owners;
        }
    }
}