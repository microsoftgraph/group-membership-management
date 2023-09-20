// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using TeamsChannelMembershipObtainer.Service.Contracts;
using Models.Entities;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class ChannelValidatorFunction
    {
        private readonly ITeamsChannelService _teamsChannelService;
        private readonly ILoggingRepository _loggingRepository;

        public ChannelValidatorFunction(ILoggingRepository loggingRepository, ITeamsChannelService teamsChannelService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelService = teamsChannelService ?? throw new ArgumentNullException(nameof(teamsChannelService));
        }

        [FunctionName(nameof(ChannelValidatorFunction))]
        public async Task<(AzureADTeamsChannel parsedChannel, bool isValid)> ValidateChannelAsync([ActivityTrigger] ChannelSyncInfo channelSyncInfo)
        {
            var runId = channelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ChannelValidatorFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            var validated = await _teamsChannelService.VerifyChannelAsync(channelSyncInfo);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ChannelValidatorFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);

            return validated;
        }
    }
}
