// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models.ServiceBus;
using Repositories.Contracts.Helpers;
using Services.Contracts;

namespace Hosts.SyncJobUpdater
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILogger<JobStatusUpdaterFunction> _logger;
        private readonly ISyncJobUpdaterService _syncJobUpdaterService;

        public JobStatusUpdaterFunction(ILogger<JobStatusUpdaterFunction> logger, ISyncJobUpdaterService syncJobUpdaterService)
        {
            _logger = logger;
            _syncJobUpdaterService = syncJobUpdaterService;
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdateQueueMessage message)
        {
            if (message != null && message.JobId != Guid.Empty)
            {
                using (_logger.BeginSyncJobScope(message.SyncJob))
                {
                    _logger.FunctionStarted(nameof(JobStatusUpdaterFunction));
                    await _syncJobUpdaterService.UpdateSyncJobStatusAsync(message);
                    _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
                }
            }
        }
    }
}