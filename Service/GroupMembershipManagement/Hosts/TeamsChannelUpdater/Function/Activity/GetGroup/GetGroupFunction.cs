// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Services.TeamsChannelUpdater.Contracts;

namespace Hosts.TeamsChannelUpdater
{
    public class GetGroupFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public GetGroupFunction(ILoggingRepository loggingRepository, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [Function(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupNameAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            _teamsChannelUpdaterService.RunId = syncJob.RunId ?? Guid.Empty;
            var groupId = await _teamsChannelUpdaterService.GetGroupIdAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            return groupId;
        }
    }
}
