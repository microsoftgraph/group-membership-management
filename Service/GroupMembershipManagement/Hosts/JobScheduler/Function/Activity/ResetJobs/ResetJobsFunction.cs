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
    public class ResetJobsFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public ResetJobsFunction(IJobSchedulingService jobSchedulingService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [Function(nameof(ResetJobsFunction))]
        public async Task<List<DistributionSyncJob>> ResetJobsAsync([ActivityTrigger] ResetJobsRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ResetJobsFunction)} function started at: {DateTime.UtcNow}" });
            var updatedJobs = await _jobSchedulingService.ResetJobsAsync(request.JobsToReset, request.DaysToAddForReset);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ResetJobsFunction)} function completed at: {DateTime.UtcNow}" });

            return updatedJobs;
        }
    }
}
