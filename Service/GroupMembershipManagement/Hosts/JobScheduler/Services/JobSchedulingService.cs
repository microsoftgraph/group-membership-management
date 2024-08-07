// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class JobSchedulingService : IJobSchedulingService
    {

        private const int MINUTES_IN_HOUR = 60;
        private const int SECONDS_IN_MINUTE = 60;


        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IRuntimeRetrievalService _runtimeRetrievalService;
        private readonly ILoggingRepository _loggingRepository;

        public JobSchedulingService(
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            IRuntimeRetrievalService runtimeRetrievalService,
            ILoggingRepository loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository;
            _runtimeRetrievalService = runtimeRetrievalService;
            _loggingRepository = loggingRepository;
        }

        public async Task<List<DistributionSyncJob>> ResetJobsAsync(List<DistributionSyncJob> jobs, int daysToAddForReset)
        {
            var newStartTime = DateTime.UtcNow.AddDays(daysToAddForReset);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Updating {jobs.Count} jobs to have ScheduledDate of {newStartTime}" });

            List<DistributionSyncJob> updatedJobs = ResetJobStartTimes(jobs, newStartTime);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Updated {jobs.Count} jobs to have ScheduledDate of {newStartTime}" });

            return updatedJobs;
        }

        public async Task<List<DistributionSyncJob>> DistributeJobsAsync(List<DistributionSyncJob> jobs, int startTimeDelayMinutes, int delayBetweenSyncsSeconds)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Distributing {jobs.Count} jobs" });

            List<DistributionSyncJob> updatedJobs = await DistributeJobStartTimesAsync(jobs, startTimeDelayMinutes, delayBetweenSyncsSeconds);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Distributed {jobs.Count} jobs" });

            return updatedJobs;
        }

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            var includeFutureScheduledJobs = true;
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsAsync(includeFutureScheduledJobs, SyncStatus.All);
            return jobs.ToList();
        }

        public async Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> updatedSyncJobs)
        {
            var jobs = MapUpdateMergeSyncJobsToEntities(updatedSyncJobs);
            await _databaseSyncJobsRepository.BatchUpdateSyncJobsAsync(jobs);
        }

        private SyncJob MapUpdateMergeSyncJobToEntity(UpdateMergeSyncJob updateMergeSyncJob)
        {
            return new SyncJob
            {
                Id = updateMergeSyncJob.Id,
                ScheduledDate = updateMergeSyncJob.ScheduledDate
            };
        }

        private List<SyncJob> MapUpdateMergeSyncJobsToEntities(IEnumerable<UpdateMergeSyncJob> jobs)
        {
            return jobs.Select(x => MapUpdateMergeSyncJobToEntity(x)).ToList();
        }

        public List<DistributionSyncJob> ResetJobStartTimes(List<DistributionSyncJob> syncJobsToReset, DateTime newStartTime)
        {
            List<DistributionSyncJob> updatedSyncJobs = syncJobsToReset.Select(job =>
            {
                job.ScheduledDate = newStartTime;
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
            Dictionary<string, double> runtimeMap = await _runtimeRetrievalService.GetRunTimesInSecondsAsync();

            // Get a period to syncs mapping
            Dictionary<int, List<DistributionSyncJob>> periodToJobs = new Dictionary<int, List<DistributionSyncJob>>();
            foreach (DistributionSyncJob job in syncJobsToDistribute)
            {
                if (!periodToJobs.ContainsKey(job.Period))
                {
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
            Dictionary<string, double> runtimeMap)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Calculating distribution for jobs with period {periodInHours}" });

            HashSet<string> groupDestinationsForPeriod = new HashSet<string>(jobsToDistribute.ConvertAll(job => job.Destination));
            runtimeMap = new Dictionary<string, double>(runtimeMap.Where(entry => groupDestinationsForPeriod.Contains(entry.Key) || entry.Key == "Default"));

            // Sort sync jobs by Status, LastRunTime
            jobsToDistribute.Sort();

            double totalTimeInSeconds = runtimeMap.Values.Sum() + (jobsToDistribute.Count - runtimeMap.Count) * runtimeMap["Default"];

            int concurrencyNumber = (int)Math.Ceiling(totalTimeInSeconds / (periodInHours * MINUTES_IN_HOUR * SECONDS_IN_MINUTE));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Calculated {concurrencyNumber} thread count for jobs with period {periodInHours}" });

            List<DateTime> jobThreads = new List<DateTime>(concurrencyNumber);
            DateTime startTime = DateTime.UtcNow.AddMinutes(startTimeDelayMinutes);
            for (int index = 0; index < concurrencyNumber; index++)
            {
                jobThreads.Add(startTime);
            }

            List<DistributionSyncJob> updatedSyncJobs = new List<DistributionSyncJob>();
            foreach (DistributionSyncJob job in jobsToDistribute)
            {
                DateTime earliestTime = jobThreads.Min();

                var serializedJob = JsonConvert.SerializeObject(job);
                var updatedJob = JsonConvert.DeserializeObject<DistributionSyncJob>(serializedJob);

                updatedJob.ScheduledDate = earliestTime;
                var groupRuntime = runtimeMap.ContainsKey(job.Destination) ? runtimeMap[job.Destination] : runtimeMap["Default"];
                DateTime updatedTime = earliestTime.AddSeconds(groupRuntime + bufferBetweenSyncsSeconds);
                int index = jobThreads.IndexOf(earliestTime);
                jobThreads[index] = updatedTime;

                updatedSyncJobs.Add(updatedJob);
            }

            return updatedSyncJobs;
        }
    }
}
