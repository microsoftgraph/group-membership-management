// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.JobTrigger
{
    public class GetChannelFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public GetChannelFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService)); ;
        }

        [Function(nameof(GetChannelFunction))]
        public async Task<Channel> GetChannelAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetChannelFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            _jobTriggerService.RunId = syncJob.RunId ?? Guid.Empty;
            var channel = await _jobTriggerService.GetChannelAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetChannelFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            return channel;
        }
    }
}