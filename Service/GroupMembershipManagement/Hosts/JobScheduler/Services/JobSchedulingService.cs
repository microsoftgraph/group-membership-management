// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System.Collections.Generic;
using System.Threading.Tasks;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Linq;
using Newtonsoft.Json;
using Repositories.Contracts.InjectConfig;
using Azure;

namespace Services
{
    public class JobSchedulingService : IJobSchedulingService {

        public readonly int MINUTES_IN_HOUR = 60;
        public readonly int SECONDS_IN_MINUTE = 60;

        private readonly ISyncJobRepository _syncJobRepository;
        private readonly IRuntimeRetrievalService _runtimeRetrievalService;
        private readonly ILoggingRepository _loggingRepository;

        public JobSchedulingService(
            ISyncJobRepository syncJobRepository,
            IRuntimeRetrievalService runtimeRetrievalService,
            ILoggingRepository loggingRepository)
        {
            _syncJobRepository = syncJobRepository;
            _runtimeRetrievalService = runtimeRetrievalService;
            _loggingRepository = loggingRepository;
        }

        public async Task<List<DistributionSyncJob>> ResetJobsAsync(List<DistributionSyncJob> jobs, int daysToAddForReset, bool includeFutureJobs)
        {
            var newStartTime = DateTime.UtcNow.AddDays(daysToAddForReset);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Updating {jobs.Count} jobs to have StartDate of {newStartTime}" });

            List<DistributionSyncJob> updatedJobs = ResetJobStartTimes(jobs, newStartTime, includeFutureJobs);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Updated {jobs.Count} jobs to have StartDate of {newStartTime}" });

            return updatedJobs;
        }

        public async Task<List<DistributionSyncJob>> DistributeJobsAsync(List<DistributionSyncJob> jobs, int startTimeDelayMinutes, int delayBetweenSyncsSeconds)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Distributing {jobs.Count} jobs" });

            List<DistributionSyncJob> updatedJobs = await DistributeJobStartTimesAsync(jobs, startTimeDelayMinutes, delayBetweenSyncsSeconds);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Distributed {jobs.Count} jobs" });

            return updatedJobs;
        }

        public async Task<TableSegmentBulkResult<DistributionSyncJob>> GetSyncJobsSegmentAsync(
            AsyncPageable<SyncJob> pageableQueryResult,
            string continuationToken,
            bool includeFutureJobs)
        {
            if (pageableQueryResult == null)
            {
                pageableQueryResult = _syncJobRepository.GetPageableQueryResult(SyncStatus.All, includeFutureJobs);
            }

            var queryResultSegment = await _syncJobRepository.GetSyncJobsSegmentAsync(pageableQueryResult, continuationToken, false);
 
            return queryResultSegment;
        }

        public async Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> updatedSyncJobs)
        {
            await _syncJobRepository.BatchUpdateSyncJobsAsync(updatedSyncJobs);
        }

        public List<DistributionSyncJob> ResetJobStartTimes(List<DistributionSyncJob> syncJobsToReset, DateTime newStartTime, bool includeFutureStartDates = false)
        {
            List<DistributionSyncJob> updatedSyncJobs = syncJobsToReset.Select(job => {
                if (includeFutureStartDates || job.StartDate.CompareTo(newStartTime) < 0)
                    job.StartDate = newStartTime;
                return job;
            }).ToList();

            return updatedSyncJobs;
        }

        public async Task<List<DistributionSyncJob>> DistributeJobStartTimesAsync(
            List<DistributionSyncJob> syncJobsToDistribute,
            int startTimeDelayMinutes,
            int bufferBetweenSyncsSeconds)
        {
            // Get all runtimes for destination groups
            List<Guid> groupIds = syncJobsToDistribute.Select(job => job.TargetOfficeGroupId).ToList();
            Dictionary<Guid, double> runtimeMap = await _runtimeRetrievalService.GetRunTimesInSecondsAsync(groupIds);

            // Get a period to syncs mapping
            Dictionary<int, List<DistributionSyncJob>> periodToJobs = new Dictionary<int, List<DistributionSyncJob>>();
            foreach (DistributionSyncJob job in syncJobsToDistribute)
            {
                if (!periodToJobs.ContainsKey(job.Period)) {
                    periodToJobs.Add(job.Period, new List<DistributionSyncJob>() { job });
                }
                else
                {
                    periodToJobs[job.Period].Add(job);
                }
            }


            List<DistributionSyncJob> updatedJobs = new List<DistributionSyncJob>();
            // Distribute jobs for each period
            foreach (int period in periodToJobs.Keys)
            {
                var jobsForPeriod = periodToJobs[period];

                List<DistributionSyncJob> updatedJobsForPeriod = await DistributeJobStartTimesForPeriod(jobsForPeriod, period, startTimeDelayMinutes, bufferBetweenSyncsSeconds, runtimeMap);
                updatedJobs.AddRange(updatedJobsForPeriod);
            }

            return updatedJobs;
        }

        private async Task<List<DistributionSyncJob>> DistributeJobStartTimesForPeriod(
            List<DistributionSyncJob> jobsToDistribute,
            int periodInHours,
            int startTimeDelayMinutes,
            int bufferBetweenSyncsSeconds,
            Dictionary<Guid, double> runtimeMap)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Calculating distribution for jobs with period {periodInHours}" });

            HashSet<Guid> groupIdsForPeriod = new HashSet<Guid>(jobsToDistribute.ConvertAll(job => job.TargetOfficeGroupId));
            runtimeMap = new Dictionary<Guid, double>(runtimeMap.Where(entry => groupIdsForPeriod.Contains(entry.Key) || entry.Key == Guid.Empty));

            // Sort sync jobs by Status, LastRunTime
            jobsToDistribute.Sort();

            double totalTimeInSeconds = runtimeMap.Values.Sum() + (jobsToDistribute.Count - runtimeMap.Count) * runtimeMap[Guid.Empty];

            int concurrencyNumber = (int) Math.Ceiling(totalTimeInSeconds / (periodInHours * MINUTES_IN_HOUR * SECONDS_IN_MINUTE));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Calculated {concurrencyNumber} thread count for jobs with period {periodInHours}" });

            List<DateTime> jobThreads = new List<DateTime>(concurrencyNumber);
            DateTime startTime = DateTime.UtcNow.AddMinutes(startTimeDelayMinutes);
            for(int index = 0; index < concurrencyNumber; index++)
            {
                jobThreads.Add(startTime);
            }

            List<DistributionSyncJob> updatedSyncJobs = new List<DistributionSyncJob>();
            foreach (DistributionSyncJob job in jobsToDistribute)
            {
                DateTime earliestTime = jobThreads.Min();

                var serializedJob = JsonConvert.SerializeObject(job);
                var updatedJob = JsonConvert.DeserializeObject<DistributionSyncJob>(serializedJob);

                updatedJob.StartDate = earliestTime;
                var groupRuntime = runtimeMap.ContainsKey(job.TargetOfficeGroupId) ? runtimeMap[job.TargetOfficeGroupId] : runtimeMap[Guid.Empty];
                DateTime updatedTime = earliestTime.AddSeconds(groupRuntime + bufferBetweenSyncsSeconds);
                int index = jobThreads.IndexOf(earliestTime);
                jobThreads[index] = updatedTime;

                updatedSyncJobs.Add(updatedJob);
            }

            return updatedSyncJobs;
        }
    }
}
