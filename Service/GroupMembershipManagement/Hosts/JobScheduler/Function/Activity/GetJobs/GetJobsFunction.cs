// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using System.Linq;
using Entities;

namespace Hosts.JobScheduler
{
    public class GetJobsFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService;
        private readonly ILogger<GetJobsFunction> _logger;

        public GetJobsFunction(IJobSchedulingService jobSchedulingService, ILogger<GetJobsFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [Function(nameof(GetJobsFunction))]
        public async Task<GetJobsResponse> GetJobsToUpdateAsync([ActivityTrigger] object request)
        {
            _logger.FunctionStarted(nameof(GetJobsFunction));
            var tableQuerySegment = await _jobSchedulingService.GetSyncJobsAsync();
            _logger.FunctionCompleted(nameof(GetJobsFunction));

            return new GetJobsResponse
            {
                JobsSegment = tableQuerySegment.Select(x => new DistributionSyncJob(x)).ToList()
            };
        }
    }
}
