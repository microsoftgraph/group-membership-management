// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class TopicMessageSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ISyncJobTopicService _syncJobTopicService = null;
        public TopicMessageSenderFunction(ILoggingRepository loggingRepository, ISyncJobTopicService syncJobService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobTopicService = syncJobService ?? throw new ArgumentNullException(nameof(syncJobService)); ;
        }

        [FunctionName(nameof(TopicMessageSenderFunction))]
        public async Task SendMessage([ActivityTrigger] SyncJob syncJob, ILogger log)
        {
            if (syncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TopicMessageSenderFunction)} function started", RunId = syncJob.RunId });
                await _syncJobTopicService.SendMessageAsync(syncJob);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TopicMessageSenderFunction)} function completed", RunId = syncJob.RunId });
            }
        }
    }
}