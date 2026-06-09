// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GroupNameReaderFunction
    {
        private readonly ILogger<GroupNameReaderFunction> _logger;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public GroupNameReaderFunction(ILogger<GroupNameReaderFunction> logger, IGraphUpdaterService graphUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [Function(nameof(GroupNameReaderFunction))]
        public async Task<string> GetGroupNameAsync([ActivityTrigger] GroupNameReaderRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(GroupNameReaderFunction));
            var groupName = await _graphUpdaterService.GetGroupNameAsync(request.GroupId);
            _logger.FunctionCompleted(nameof(GroupNameReaderFunction));

            return groupName;
        }
    }
}