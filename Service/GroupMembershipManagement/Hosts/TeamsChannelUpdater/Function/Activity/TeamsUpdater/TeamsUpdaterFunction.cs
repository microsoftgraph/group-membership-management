// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models.Entities;

namespace Hosts.TeamsChannelUpdater
{
    public class TeamsUpdaterFunction
    {
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;
        private readonly ILogger<TeamsUpdaterFunction> _logger;

        public TeamsUpdaterFunction(
            ITeamsChannelUpdaterService teamsChannelUpdaterService,
            ILogger<TeamsUpdaterFunction> logger)
        {
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(TeamsUpdaterFunction))]
        public async Task<TeamsUpdaterResponse> RunAsync([ActivityTrigger] TeamsUpdaterRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob);

            _logger.FunctionStarted(nameof(TeamsUpdaterFunction));

            var successCount = 0;
            var usersToRetry = new List<AzureADTeamsUser>();
            var usersNotFound = new List<AzureADTeamsUser>();
            var usersAlreadyExist = new List<AzureADTeamsUser>();
            var teamsChannelInfo = request.TeamsChannelInfo;

            if (request.Type == RequestType.Add)
            {
                var addUsersToChannelResponse = await _teamsChannelUpdaterService.AddUsersToChannelAsync(teamsChannelInfo, request.Members);

                successCount = addUsersToChannelResponse.SuccessCount;
                usersToRetry = addUsersToChannelResponse.UsersToRetry;
            }
            else if (request.Type == RequestType.Remove)
            {
                var removeUsersFromChannel = await _teamsChannelUpdaterService.RemoveUsersFromChannelAsync(teamsChannelInfo, request.Members);

                successCount = removeUsersFromChannel.SuccessCount;
                usersNotFound = removeUsersFromChannel.UserRemovesFailed;
            }

            _logger.FunctionCompleted(nameof(TeamsUpdaterFunction));

            return new TeamsUpdaterResponse()
            {
                SuccessCount = successCount,
                UsersToRetry = usersToRetry,
                UsersNotFound = usersNotFound,
                UsersAlreadyExist = usersAlreadyExist
            };
        }
    }
}

