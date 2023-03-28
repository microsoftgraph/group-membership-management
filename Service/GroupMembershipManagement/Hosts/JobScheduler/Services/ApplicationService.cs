// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Core;
using Entities;
using Models;
using Repositories.Contracts;
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
        private readonly ILoggingRepository _loggingRepository;

        public ApplicationService(IJobSchedulingService jobSchedulingService, IJobSchedulerConfig jobSchedulerConfig, ILoggingRepository loggingRepository)
        {
            _jobSchedulingService = jobSchedulingService;
            _jobSchedulerConfig = jobSchedulerConfig;
            _loggingRepository = loggingRepository;
        }


        public async Task RunAsync()
        {
            if(!_jobSchedulerConfig.ResetJobs && !_jobSchedulerConfig.DistributeJobs)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Configuration set to not reset or update so doing nothing." });

                return;
            }

            List<DistributionSyncJob> jobsToUpdate = await GetSyncJobsAsync(_jobSchedulerConfig.IncludeFutureJobs);
            List<DistributionSyncJob> jobsWithUpdates = null;

            if (_jobSchedulerConfig.ResetJobs)
            {
                var newStartTime = DateTime.UtcNow.AddDays(_jobSchedulerConfig.DaysToAddForReset);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resetting {jobsToUpdate.Count} jobs to have StartDate of {newStartTime}" });

                jobsWithUpdates = await _jobSchedulingService.ResetJobsAsync(jobsToUpdate, _jobSchedulerConfig.DaysToAddForReset, _jobSchedulerConfig.IncludeFutureJobs);

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Reset {jobsToUpdate.Count} jobs to have StartDate of {newStartTime}" });
            }

            else if (_jobSchedulerConfig.DistributeJobs)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Distributing {jobsToUpdate.Count} jobs" });

                jobsWithUpdates = await _jobSchedulingService.DistributeJobsAsync(jobsToUpdate, _jobSchedulerConfig.StartTimeDelayMinutes, _jobSchedulerConfig.DelayBetweenSyncsSeconds);

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Distributed {jobsToUpdate.Count} jobs" });
            }

            if (jobsWithUpdates != null && jobsWithUpdates.Count > 0)
            {
                await UpdateSyncJobsAsync(jobsWithUpdates);
            }

        }

        private async Task<List<DistributionSyncJob>> GetSyncJobsAsync(bool includeFutureJobs)
        {
            AsyncPageable<SyncJob> pageableQueryResult = null;
            string continuationToken = null;

            var jobs = new List<DistributionSyncJob>();
            do
            {
                var tableQuerySegment = await _jobSchedulingService.GetSyncJobsSegmentAsync(pageableQueryResult, continuationToken, includeFutureJobs);

                jobs.AddRange(tableQuerySegment.Results);

                pageableQueryResult = tableQuerySegment.PageableQueryResult;
                continuationToken = tableQuerySegment.ContinuationToken;

            } while (continuationToken != null);

            return jobs;
        }

        private async Task UpdateSyncJobsAsync(List<DistributionSyncJob> jobsToUpdate)
        {
            var BATCH_SIZE = 100;
            var groupingsByPartitionKey = jobsToUpdate.GroupBy(x => x.PartitionKey);

            var batchTasks = new List<Task>();

            foreach (var grouping in groupingsByPartitionKey)
            {
                var jobsBatches = grouping.Select((x, idx) => new { x, idx })
                .GroupBy(x => x.idx / BATCH_SIZE)
                .Select(g => g.Select(a => a.x));

                foreach (var batch in jobsBatches)
                {
                    batchTasks.Add(_jobSchedulingService.BatchUpdateSyncJobsAsync(batch));
                }
            }

            await Task.WhenAll(batchTasks);
        }
    }
}
