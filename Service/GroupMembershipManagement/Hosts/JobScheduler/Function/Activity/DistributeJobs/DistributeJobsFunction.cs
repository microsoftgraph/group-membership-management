// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.Functions.Worker;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;
using System.Collections.Generic;
using Models;

namespace Hosts.JobScheduler
{
    public class DistributeJobsFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public DistributeJobsFunction(IJobSchedulingService jobSchedulingService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [Function(nameof(DistributeJobsFunction))]
        public async Task<List<DistributionSyncJob>> DistributeJobsAsync([ActivityTrigger] DistributeJobsRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DistributeJobsFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var updatedJobs = await _jobSchedulingService.DistributeJobsAsync(request.JobsToDistribute, request.StartTimeDelayMinutes, request.DelayBetweenSyncsSeconds, request.PrioritizeThresholdJobs);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DistributeJobsFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return updatedJobs;
        }
    }
}
