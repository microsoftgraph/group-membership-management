// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using Services.TeamsChannelUpdater.Contracts;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.TeamsChannelUpdater
{
    public class GroupNameReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public GroupNameReaderFunction(ILoggingRepository loggingRepository, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [Function(nameof(GroupNameReaderFunction))]
        public async Task<string> GetGroupNameAsync([ActivityTrigger] GroupNameReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupNameReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _teamsChannelUpdaterService.RunId = request.RunId;
            var groupName = await _teamsChannelUpdaterService.GetGroupNameAsync(request.GroupId, request.RunId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupNameReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return groupName;
        }
    }
}