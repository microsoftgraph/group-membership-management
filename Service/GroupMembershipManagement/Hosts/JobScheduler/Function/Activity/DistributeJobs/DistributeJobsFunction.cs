// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using System.Collections.Generic;
using Models;

namespace Hosts.JobScheduler
{
    public class DistributeJobsFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService;
        private readonly ILogger<DistributeJobsFunction> _logger;

        public DistributeJobsFunction(IJobSchedulingService jobSchedulingService, ILogger<DistributeJobsFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [Function(nameof(DistributeJobsFunction))]
        public async Task<List<DistributionSyncJob>> DistributeJobsAsync([ActivityTrigger] DistributeJobsRequest request)
        {
            _logger.FunctionStarted(nameof(DistributeJobsFunction));
            var updatedJobs = await _jobSchedulingService.DistributeJobsAsync(request.JobsToDistribute, request.StartTimeDelayMinutes, request.DelayBetweenSyncsSeconds, request.PrioritizeThresholdJobs);
            _logger.FunctionCompleted(nameof(DistributeJobsFunction));

            return updatedJobs;
        }
    }
}

