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

namespace JobTrigger.Activity.SyncJobsProcessor
{
    public class SyncJobsProcessorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ISyncJobTopicService _syncJobTopicService = null;
        public SyncJobsProcessorFunction(ILoggingRepository loggingRepository, ISyncJobTopicService syncJobService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobTopicService = syncJobService ?? throw new ArgumentNullException(nameof(syncJobService)); ;
        }

        [FunctionName(nameof(SyncJobsProcessorFunction))]
        public async Task ProcessSyncJobs([ActivityTrigger] ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SyncJobsProcessorFunction)} function started" });

            await _syncJobTopicService.ProcessSyncJobsAsync();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SyncJobsProcessorFunction)} function completed" });
        }
    }
}
