// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class QueueMessageSenderFunction
    {
        private readonly ILogger<QueueMessageSenderFunction> _logger;
        private readonly ITeamsChannelService _teamsChannelService;

        public QueueMessageSenderFunction(ILogger<QueueMessageSenderFunction> logger, ITeamsChannelService teamsChannelService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [Function(nameof(QueueMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] QueueMessageSenderRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.ChannelSyncInfo.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.ChannelSyncInfo.CurrentPart,
                ["TotalParts"] = request.ChannelSyncInfo.TotalParts
            });

            _logger.FunctionStarted(nameof(QueueMessageSenderFunction));
            await _teamsChannelService.MakeMembershipAggregatorRequestAsync(request.ChannelSyncInfo, request.FilePath);
            _logger.FunctionCompleted(nameof(QueueMessageSenderFunction));
        }
    }
}