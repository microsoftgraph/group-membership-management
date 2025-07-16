// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using Services.Contracts;
using System;

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

        [FunctionName(nameof(JobStatusUpdaterFunction))]
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