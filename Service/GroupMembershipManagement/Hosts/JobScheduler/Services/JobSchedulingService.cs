// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System.Collections.Generic;
using System.Threading.Tasks;
using Repositories.Contracts;
using Microsoft.ApplicationInsights;
using Services.Contracts;
using System;
using System.Linq;
using Services.Entities;
using Newtonsoft.Json;
using Repositories.Contracts.InjectConfig;

namespace Services
{
    public class JobSchedulingService : IJobSchedulingService {

        public readonly int START_TIME_DELAY_MINUTES;
        public readonly int BUFFER_SECONDS;
        public readonly int MINUTES_IN_HOUR = 60;
        public readonly int SECONDS_IN_MINUTE = 60;

        private readonly ISyncJobRepository _syncJobRepository;
        private readonly IRuntimeRetrievalService _runtimeRetrievalService;
        private readonly ILoggingRepository _loggingRepository;

        public JobSchedulingService(
            IJobSchedulerConfig jobSchedulerConfig,
            ISyncJobRepository syncJobRepository,
            IRuntimeRetrievalService runtimeRetrievalService,
            ILoggingRepository loggingRepository)
        {
            START_TIME_DELAY_MINUTES = jobSchedulerConfig.StartTimeDelayMinutes;
            BUFFER_SECONDS = jobSchedulerConfig.DelayBetweenSyncsSeconds;
            _syncJobRepository = syncJobRepository;
            _runtimeRetrievalService = runtimeRetrievalService;
            _loggingRepository = loggingRepository;
        }

        public async Task<List<SchedulerSyncJob>> GetAllSyncJobsAsync(bool includeFutureStartDates = false)
        {
            var message = "Getting enabled sync jobs" + (includeFutureStartDates ? " including those with future StartDate values" : "");
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = message });

            var schedulerSyncJobs = new List<SchedulerSyncJob>();
            var jobs = _syncJobRepository.GetSyncJobsAsync(SyncStatus.All, false, false);
            await foreach (var job in jobs)
            {
                if (includeFutureStartDates || job.StartDate.CompareTo(DateTime.UtcNow) < 0)
                {
                    var serializedJob = JsonConvert.SerializeObject(job);
                    SchedulerSyncJob schedulerJob = JsonConvert.DeserializeObject<SchedulerSyncJob>(serializedJob);
                    schedulerSyncJobs.Add(schedulerJob);
                }
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Found {schedulerSyncJobs.Count} jobs to update" });
            return schedulerSyncJobs;
        }

        public async Task UpdateSyncJobsAsync(List<SchedulerSyncJob> updatedSyncJobs)
        {
            await _syncJobRepository.UpdateSyncJobsAsync(updatedSyncJobs);
        }

        public List<SchedulerSyncJob> ResetJobStartTimes(List<SchedulerSyncJob> schedulerSyncJobs, DateTime newStartTime, bool includeFutureStartDates = false)
        {
            List<SchedulerSyncJob> updatedSyncJobs = schedulerSyncJobs.Select(job => {
                if (includeFutureStartDates || job.StartDate.CompareTo(newStartTime) < 0)
                    job.StartDate = newStartTime;
                return job;
            }).ToList();

            return updatedSyncJobs;
        }

        public async Task<List<SchedulerSyncJob>> DistributeJobStartTimesAsync(List<SchedulerSyncJob> schedulerSyncJobs)
        {
            // Get all runtimes for destination groups
            List<Guid> groupIds = schedulerSyncJobs.Select(job => job.TargetOfficeGroupId).ToList();
            Dictionary<Guid, double> runtimeMap = await _runtimeRetrievalService.GetRuntimesInSeconds(groupIds);

            // Get a period to syncs mapping
            Dictionary<int, List<SchedulerSyncJob>> periodToJobs = new Dictionary<int, List<SchedulerSyncJob>>();
            foreach (SchedulerSyncJob job in schedulerSyncJobs)
            {
                if (!periodToJobs.ContainsKey(job.Period)) {
                    periodToJobs.Add(job.Period, new List<SchedulerSyncJob>() { job });
                }
                else
                {
                    periodToJobs[job.Period].Add(job);
                }
            }


            List<SchedulerSyncJob> updatedJobs = new List<SchedulerSyncJob>();
            // Distribute jobs for each period
            foreach (int period in periodToJobs.Keys)
            {
                var jobsForPeriod = periodToJobs[period];

                List<SchedulerSyncJob> updatedJobsForPeriod = await DistributeJobStartTimesForPeriod(jobsForPeriod, period, runtimeMap);
                updatedJobs.AddRange(updatedJobsForPeriod);
            }

            return updatedJobs;
        }

        private async Task<List<SchedulerSyncJob>> DistributeJobStartTimesForPeriod(
            List<SchedulerSyncJob> schedulerSyncJobs, 
            int periodInHours,
            Dictionary<Guid, double> runtimeMap)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Calculating distribution for jobs with period {periodInHours}" });

            HashSet<Guid> groupIdsForPeriod = new HashSet<Guid>(schedulerSyncJobs.ConvertAll(job => job.TargetOfficeGroupId));
            runtimeMap = new Dictionary<Guid, double>(runtimeMap.Where(entry => groupIdsForPeriod.Contains(entry.Key)));

            // Sort sync jobs by Status, LastRunTime
            schedulerSyncJobs.Sort();

            double totalTimeInSeconds = runtimeMap.Values.Sum();

            int concurrencyNumber = (int) Math.Ceiling(totalTimeInSeconds / (periodInHours * MINUTES_IN_HOUR * SECONDS_IN_MINUTE));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Calculated {concurrencyNumber} thread count for jobs with period {periodInHours}" });

            List<DateTime> jobThreads = new List<DateTime>(concurrencyNumber);
            DateTime startTime = DateTime.UtcNow.AddMinutes(START_TIME_DELAY_MINUTES);
            for(int index = 0; index < concurrencyNumber; index++)
            {
                jobThreads.Add(startTime);
            }

            List<SchedulerSyncJob> updatedSyncJobs = new List<SchedulerSyncJob>();
            foreach(SchedulerSyncJob job in schedulerSyncJobs)
            {
                DateTime earliestTime = jobThreads.Min();

                var serializedJob = JsonConvert.SerializeObject(job);
                SchedulerSyncJob updatedJob = JsonConvert.DeserializeObject<SchedulerSyncJob>(serializedJob);

                updatedJob.StartDate = earliestTime;
                DateTime updatedTime = earliestTime.AddSeconds(runtimeMap[updatedJob.TargetOfficeGroupId] + BUFFER_SECONDS);
                int index = jobThreads.IndexOf(earliestTime);
                jobThreads[index] = updatedTime;

                updatedSyncJobs.Add(updatedJob);
            }

            return updatedSyncJobs;
        }
    }
}
