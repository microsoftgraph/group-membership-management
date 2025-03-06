// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;
using Models.Entities;
using System.Collections.Generic;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class UserReaderFunction
    {
        private readonly ITeamsChannelService _teamsChannelService;
        private readonly ILoggingRepository _loggingRepository;

        public UserReaderFunction(ILoggingRepository loggingRepository, ITeamsChannelService teamsChannelService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [FunctionName(nameof(UserReaderFunction))]
        public async Task<List<AzureADTeamsUser>> ReadUsersAsync([ActivityTrigger] UserReaderRequest request)
        {
            var runId = request.RunId;

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UserReaderFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            var users = await _teamsChannelService.GetUsersFromTeamAsync(request.Channel, runId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Read {users.Count} users from {request.ChannelSyncInfo.SyncJob.Destination}.", RunId = runId });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UserReaderFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);

            return users;
        }
    }
}
