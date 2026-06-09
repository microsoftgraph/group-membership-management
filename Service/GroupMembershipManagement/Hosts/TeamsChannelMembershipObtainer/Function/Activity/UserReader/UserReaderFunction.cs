// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;
using Models.Entities;
using System.Collections.Generic;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class UserReaderFunction
    {
        private readonly ILogger<UserReaderFunction> _logger;
        private readonly ITeamsChannelService _teamsChannelService;

        public UserReaderFunction(ILogger<UserReaderFunction> logger, ITeamsChannelService teamsChannelService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [Function(nameof(UserReaderFunction))]
        public async Task<List<AzureADTeamsUser>> ReadUsersAsync([ActivityTrigger] UserReaderRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.ChannelSyncInfo.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.ChannelSyncInfo.CurrentPart,
                ["TotalParts"] = request.ChannelSyncInfo.TotalParts
            });

            _logger.FunctionStarted(nameof(UserReaderFunction));

            var users = await _teamsChannelService.GetUsersFromTeamAsync(request.Channel, request.RunId);

            _logger.UsersRead(users.Count, request.Channel.ObjectId, request.Channel.ChannelId);
            _logger.FunctionCompleted(nameof(UserReaderFunction));

            return users;
        }
    }
}
