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
    public class ResetJobsFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService;
        private readonly ILogger<ResetJobsFunction> _logger;

        public ResetJobsFunction(IJobSchedulingService jobSchedulingService, ILogger<ResetJobsFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [Function(nameof(ResetJobsFunction))]
        public async Task<List<DistributionSyncJob>> ResetJobsAsync([ActivityTrigger] ResetJobsRequest request)
        {
            _logger.FunctionStarted(nameof(ResetJobsFunction));
            var updatedJobs = await _jobSchedulingService.ResetJobsAsync(request.JobsToReset, request.DaysToAddForReset);
            _logger.FunctionCompleted(nameof(ResetJobsFunction));

            return updatedJobs;
        }
    }
}
