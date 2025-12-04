// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using Services.Contracts;

namespace Hosts.SyncJobUpdater
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobUpdaterService _syncJobUpdaterService;

        public JobStatusUpdaterFunction(ILoggingRepository loggingRepository, ISyncJobUpdaterService syncJobUpdaterService)
        {
            _loggingRepository = loggingRepository;
            _syncJobUpdaterService = syncJobUpdaterService;
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdateQueueMessage message)
        {
            if (message != null && message.JobId != Guid.Empty)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = message.RunId }, VerbosityLevel.DEBUG);
                await _syncJobUpdaterService.UpdateSyncJobStatusAsync(message);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = message.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}