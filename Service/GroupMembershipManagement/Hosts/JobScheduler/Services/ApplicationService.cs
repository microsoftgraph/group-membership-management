// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;
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
        private readonly IJobSchedulerConfig _jobSchedulerConfig;

        public ApplicationService(IJobSchedulingService jobSchedulingService, IJobSchedulerConfig jobSchedulerConfig)
        {
            _jobSchedulingService = jobSchedulingService;
            _jobSchedulerConfig = jobSchedulerConfig;
        }


        public async Task RunAsync()
        {
            List<SchedulerSyncJob> jobs = await _jobSchedulingService.GetAllSyncJobsAsync(_jobSchedulerConfig.IncludeFutureJobs);

            if (_jobSchedulerConfig.ResetJobs)
            {
                List<SchedulerSyncJob> updatedJobs = _jobSchedulingService.ResetJobStartTimes(jobs, DateTime.UtcNow.AddDays(_jobSchedulerConfig.DaysToAddForReset), _jobSchedulerConfig.IncludeFutureJobs);
                await _jobSchedulingService.UpdateSyncJobsAsync(updatedJobs);
            }

            if (_jobSchedulerConfig.DistributeJobs)
            {
                List<SchedulerSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(jobs);
                await _jobSchedulingService.UpdateSyncJobsAsync(updatedJobs);
            }
        }
    }
}
