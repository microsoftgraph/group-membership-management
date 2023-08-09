// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class GroupOwnersReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public GroupOwnersReaderFunction(ILoggingRepository loggingRepository, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [FunctionName(nameof(GroupOwnersReaderFunction))]
        public async Task<List<AzureADUser>> GetGroupOwnersAsync([ActivityTrigger] GroupOwnersReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupOwnersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _teamsChannelUpdaterService.RunId = request.RunId;
            var owners = await _teamsChannelUpdaterService.GetGroupOwnersAsync(request.GroupId, request.RunId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupOwnersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return owners;
        }
    }
}