// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
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
        private readonly ILoggingRepository _loggingRepository;

        public ApplicationService(IJobSchedulingService jobSchedulingService, IJobSchedulerConfig jobSchedulerConfig, ILoggingRepository loggingRepository)
        {
            _jobSchedulingService = jobSchedulingService;
            _jobSchedulerConfig = jobSchedulerConfig;
            _loggingRepository = loggingRepository;
        }


        public async Task RunAsync()
        {
            List<SchedulerSyncJob> jobs = await _jobSchedulingService.GetAllSyncJobsAsync(_jobSchedulerConfig.IncludeFutureJobs);

            if (_jobSchedulerConfig.ResetJobs)
            {
                var newStartTime = DateTime.UtcNow.AddDays(_jobSchedulerConfig.DaysToAddForReset);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Updating {jobs.Count} jobs to have StartDate of {newStartTime}" });

                List<SchedulerSyncJob> updatedJobs = _jobSchedulingService.ResetJobStartTimes(jobs, newStartTime, _jobSchedulerConfig.IncludeFutureJobs);
                await _jobSchedulingService.UpdateSyncJobsAsync(updatedJobs);
 
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Updated {jobs.Count} jobs to have StartDate of {newStartTime}" });
            }

            if (_jobSchedulerConfig.DistributeJobs)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Distributing {jobs.Count} jobs" });

                List<SchedulerSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(jobs);
                await _jobSchedulingService.UpdateSyncJobsAsync(updatedJobs);

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Distributed {jobs.Count} jobs" });
            }
        }
    }
}
