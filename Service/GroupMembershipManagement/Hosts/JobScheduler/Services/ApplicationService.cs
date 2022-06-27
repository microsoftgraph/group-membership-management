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
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Starting JobScheduler console app." });

            var jobs = await _jobSchedulingService.GetAllSyncJobsAsync(_jobSchedulerConfig.IncludeFutureJobs);

            if (_jobSchedulerConfig.ResetJobs)
            {
                await _jobSchedulingService.ResetJobs(jobs);   
            }

            if (_jobSchedulerConfig.DistributeJobs)
            {
                await _jobSchedulingService.DistributeJobs(jobs);
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Completed JobScheduler console app." });
        }
    }
}
