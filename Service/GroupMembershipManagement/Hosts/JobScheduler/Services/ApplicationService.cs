// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Core;
using Hosts.JobScheduler;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services
{
    public class ApplicationService : IApplicationService
    {
        private readonly IJobSchedulingService _jobSchedulingService;
        private readonly IJobSchedulerConfig _jobSchedulerConfig;
        private readonly ILogger<ApplicationService> _logger;

        public ApplicationService(IJobSchedulingService jobSchedulingService, IJobSchedulerConfig jobSchedulerConfig, ILogger<ApplicationService> logger)
        {
            _jobSchedulingService = jobSchedulingService;
            _jobSchedulerConfig = jobSchedulerConfig;
            _logger = logger;
        }


        public async Task RunAsync()
        {
            if (!_jobSchedulerConfig.ResetJobs && !_jobSchedulerConfig.DistributeJobs)
            {
                _logger.ConfigurationSetToDoNothing();

                return;
            }

            List<DistributionSyncJob> jobsToUpdate = await GetSyncJobsAsync();
            List<DistributionSyncJob> jobsWithUpdates = null;

            if (_jobSchedulerConfig.ResetJobs)
            {
                var newStartTime = DateTime.UtcNow.AddDays(_jobSchedulerConfig.DaysToAddForReset);
                _logger.ResettingJobs(jobsToUpdate.Count, newStartTime);

                jobsWithUpdates = await _jobSchedulingService.ResetJobsAsync(jobsToUpdate, _jobSchedulerConfig.DaysToAddForReset);

                _logger.ResetJobs(jobsToUpdate.Count, newStartTime);
            }

            else if (_jobSchedulerConfig.DistributeJobs)
            {
                _logger.DistributingJobsInApplicationService(jobsToUpdate.Count);

                jobsWithUpdates = await _jobSchedulingService.DistributeJobsAsync(jobsToUpdate, _jobSchedulerConfig.StartTimeDelayMinutes, _jobSchedulerConfig.DelayBetweenSyncsSeconds);

                _logger.DistributedJobsInApplicationService(jobsToUpdate.Count);
            }

            if (jobsWithUpdates != null && jobsWithUpdates.Count > 0)
            {
                await UpdateSyncJobsAsync(jobsWithUpdates);
            }
        }

        private async Task<List<DistributionSyncJob>> GetSyncJobsAsync()
        {
            var jobs = await _jobSchedulingService.GetSyncJobsAsync();
            return jobs.Select(x => new DistributionSyncJob(x)).ToList();
        }

        private async Task UpdateSyncJobsAsync(List<DistributionSyncJob> jobsToUpdate)
        {
            await _jobSchedulingService.BatchUpdateSyncJobsAsync(jobsToUpdate);
        }
    }
}
