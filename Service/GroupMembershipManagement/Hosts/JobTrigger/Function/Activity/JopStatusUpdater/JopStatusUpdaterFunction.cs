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
    public class JopStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ISyncJobTopicService _syncJobTopicService = null;
        public JopStatusUpdaterFunction(ILoggingRepository loggingRepository, ISyncJobTopicService syncJobService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobTopicService = syncJobService ?? throw new ArgumentNullException(nameof(syncJobService)); ;
        }

        [FunctionName(nameof(JopStatusUpdaterFunction))]
        public async Task UpdateJobStatus([ActivityTrigger] JopStatusUpdaterRequest request, ILogger log)
        {
            
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JopStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId });
                await _syncJobTopicService.UpdateSyncJobStatusAsync(request.CanWriteToGroup, request.SyncJob);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JopStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId });
        }
    }
}