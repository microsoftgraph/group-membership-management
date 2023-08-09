// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using Services.TeamsChannelUpdater.Contracts;
using System.Collections.Generic;
using Models.Entities;

namespace Hosts.TeamsChannelUpdater
{
    public class TeamsUpdaterFunction
    {
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;
        private readonly ILoggingRepository _loggingRepository;

        public TeamsUpdaterFunction(
            ITeamsChannelUpdaterService teamsChannelUpdaterService,
            ILoggingRepository loggingRepository)
        {
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(TeamsUpdaterFunction))]
        public async Task<TeamsUpdaterResponse> RunAsync([ActivityTrigger] TeamsUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TeamsUpdaterFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            _teamsChannelUpdaterService.RunId = request.RunId;

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

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TeamsUpdaterFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

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
