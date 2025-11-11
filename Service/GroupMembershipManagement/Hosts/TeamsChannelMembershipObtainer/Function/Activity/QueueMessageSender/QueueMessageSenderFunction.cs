// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class QueueMessageSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ITeamsChannelService _teamsChannelService;
        public QueueMessageSenderFunction(ILoggingRepository loggingRepository, ITeamsChannelService teamsChannelService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [Function(nameof(QueueMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] QueueMessageSenderRequest request)
        {
            var syncJob = request.ChannelSyncInfo.SyncJob;

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(QueueMessageSenderFunction)} function started",
                RunId = syncJob.RunId
            }, VerbosityLevel.DEBUG);

            await _teamsChannelService.MakeMembershipAggregatorRequestAsync(request.ChannelSyncInfo, request.FilePath);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(QueueMessageSenderFunction)} function completed",
                RunId = syncJob.RunId
            }, VerbosityLevel.DEBUG);
        }
    }
}