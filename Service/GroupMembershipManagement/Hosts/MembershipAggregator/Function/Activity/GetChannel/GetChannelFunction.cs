// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class GetChannelFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDeltaCalculatorService _deltaCalculatorService = null;

        public GetChannelFunction(ILoggingRepository loggingRepository, IDeltaCalculatorService deltaCalculatorService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _deltaCalculatorService = deltaCalculatorService ?? throw new ArgumentNullException(nameof(deltaCalculatorService));
        }

        [Function(nameof(GetChannelFunction))]
        public async Task<string> GetChannelAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetChannelFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            _deltaCalculatorService.RunId = syncJob.RunId ?? Guid.Empty;
            var channelId = syncJob.MembershipType == MembershipTypes.GroupMembership.ToString() ? string.Empty : await _deltaCalculatorService.GetChannelIdAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetChannelFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            return channelId;
        }
    }
}