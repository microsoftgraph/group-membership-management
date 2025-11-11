// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.TeamsChannelUpdater;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class GetChannelFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public GetChannelFunction(ILoggingRepository loggingRepository, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [Function(nameof(GetChannelFunction))]
        public async Task<string> GetChannelAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetChannelFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            _teamsChannelUpdaterService.RunId = syncJob.RunId ?? Guid.Empty;
            var channelId = syncJob.MembershipType == MembershipTypes.GroupMembership.ToString() ? string.Empty : await _teamsChannelUpdaterService.GetChannelIdAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetChannelFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            return channelId;
        }
    }
}
