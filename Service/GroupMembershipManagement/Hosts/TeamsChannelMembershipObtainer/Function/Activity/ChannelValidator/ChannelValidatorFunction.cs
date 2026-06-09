// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;
using Models.Entities;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class ChannelValidatorFunction
    {
        private readonly ILogger<ChannelValidatorFunction> _logger;
        private readonly ITeamsChannelService _teamsChannelService;

        public ChannelValidatorFunction(ILogger<ChannelValidatorFunction> logger, ITeamsChannelService teamsChannelService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [Function(nameof(ChannelValidatorFunction))]
        public async Task<ValidateChannelResponse> ValidateChannelAsync([ActivityTrigger] ChannelSyncInfo channelSyncInfo)
        {
            using var scope = _logger.BeginSyncJobScope(channelSyncInfo.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = channelSyncInfo.CurrentPart,
                ["TotalParts"] = channelSyncInfo.TotalParts
            });

            _logger.FunctionStarted(nameof(ChannelValidatorFunction));
            var validated = await _teamsChannelService.VerifyChannelAsync(channelSyncInfo);
            _logger.FunctionCompleted(nameof(ChannelValidatorFunction));

            return validated;
        }
    }
}
