// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services
{
    public class ApplicationService: IApplicationService
    {
        private readonly IJobSchedulingService _jobSchedulingService;

        public ApplicationService(IJobSchedulingService jobSchedulingService)
        {
            _jobSchedulingService = jobSchedulingService;
        }


        public async Task RunAsync(bool resetJobs, bool distributeJobs, bool includeFutureJobs, int daysToAddForReset = 0)
        {
            List<SchedulerSyncJob> jobs = await _jobSchedulingService.GetAllSyncJobsAsync(includeFutureJobs);

            if (resetJobs)
            {
                List<SchedulerSyncJob> updatedJobs = _jobSchedulingService.ResetJobStartTimes(jobs, DateTime.UtcNow.AddDays(daysToAddForReset), includeFutureJobs);
                await _jobSchedulingService.UpdateSyncJobsAsync(updatedJobs);
            }

            if (distributeJobs)
            {
                List<SchedulerSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(jobs);
                await _jobSchedulingService.UpdateSyncJobsAsync(updatedJobs);
            }
        }
    }
}
