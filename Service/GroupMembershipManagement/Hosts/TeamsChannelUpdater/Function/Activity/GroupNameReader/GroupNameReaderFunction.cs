// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class GroupNameReaderFunction
    {
        private readonly ILogger<GroupNameReaderFunction> _logger;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public GroupNameReaderFunction(ILogger<GroupNameReaderFunction> logger, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [Function(nameof(GroupNameReaderFunction))]
        public async Task<string> GetGroupNameAsync([ActivityTrigger] GroupNameReaderRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob);

            _logger.FunctionStarted(nameof(GroupNameReaderFunction));
            var groupName = await _teamsChannelUpdaterService.GetGroupNameAsync(request.GroupId, request.SyncJob.RunId.GetValueOrDefault());
            _logger.FunctionCompleted(nameof(GroupNameReaderFunction));
            return groupName;
        }
    }
}
