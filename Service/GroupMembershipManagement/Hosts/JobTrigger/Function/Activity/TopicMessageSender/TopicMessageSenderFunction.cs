// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class TopicMessageSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public TopicMessageSenderFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [FunctionName(nameof(TopicMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] SyncJob syncJob)
        {
            if (syncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TopicMessageSenderFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
                await _jobTriggerService.SendMessageAsync(syncJob);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TopicMessageSenderFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}