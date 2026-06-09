// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class GetGroupFunction
    {
        private readonly ILogger<GetGroupFunction> _logger;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public GetGroupFunction(ILogger<GetGroupFunction> logger, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [Function(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupNameAsync([ActivityTrigger] SyncJob syncJob)
        {
            using var scope = _logger.BeginSyncJobScope(syncJob);

            _logger.FunctionStarted(nameof(GetGroupFunction));
            var groupId = await _teamsChannelUpdaterService.GetGroupIdAsync(syncJob);
            _logger.FunctionCompleted(nameof(GetGroupFunction));
            return groupId;
        }
    }
}
