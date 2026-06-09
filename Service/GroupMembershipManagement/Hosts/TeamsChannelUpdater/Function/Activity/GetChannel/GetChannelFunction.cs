// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class GetChannelFunction
    {
        private readonly ILogger<GetChannelFunction> _logger;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public GetChannelFunction(ILogger<GetChannelFunction> logger, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [Function(nameof(GetChannelFunction))]
        public async Task<string> GetChannelAsync([ActivityTrigger] SyncJob syncJob)
        {
            using var scope = _logger.BeginSyncJobScope(syncJob);

            _logger.FunctionStarted(nameof(GetChannelFunction));
            var channelId = syncJob.MembershipType == MembershipTypes.GroupMembership.ToString() ? string.Empty : await _teamsChannelUpdaterService.GetChannelIdAsync(syncJob);
            _logger.FunctionCompleted(nameof(GetChannelFunction));
            return channelId;
        }
    }
}
