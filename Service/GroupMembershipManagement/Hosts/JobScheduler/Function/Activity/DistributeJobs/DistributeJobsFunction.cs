// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
            var updatedJobs = await _jobSchedulingService.DistributeJobsAsync(request.JobsToDistribute, request.StartTimeDelayMinutes, request.DelayBetweenSyncsSeconds);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DistributeJobsFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return updatedJobs;
        }
    }
}
