// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class JobUpdaterFunction
    {
        private readonly ILogger<JobUpdaterFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public JobUpdaterFunction(ILogger<JobUpdaterFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(JobUpdaterFunction))]
        public async Task UpdateJobAsync([ActivityTrigger] JobUpdaterRequest request)
        {
            if (request.SyncJob != null)
            {
                using var activity = CorrelationActivity.StartSyncJobActivity(nameof(JobUpdaterFunction), request.SyncJob);
                using (_logger.BeginSyncJobScope(request.SyncJob))
                {
                    _logger.ActivityFunctionStarted(nameof(JobUpdaterFunction));
                    await _jobTriggerService.UpdateSyncJobAsync(request.Status, request.SyncJob);
                    _logger.ActivityFunctionCompleted(nameof(JobUpdaterFunction));
                }
            }
        }
    }
}
