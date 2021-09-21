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

namespace Services
{
    public class JobSchedulingService : IJobSchedulingService {

        public readonly int START_TIME_DELAY_MINUTES;
        public readonly int BUFFER_SECONDS;

        private readonly ISyncJobRepository _syncJobRepository;
        private readonly IRuntimeRetrievalService _runtimeRetrievalService;
        private readonly TelemetryClient _telemetryClient;

        public JobSchedulingService(ISyncJobRepository syncJobRepository,
            IRuntimeRetrievalService runtimeRetrievalService,
            TelemetryClient telemetryClient,
            int startTimeDelayMinutes,
            int delayBetweenSyncsSeconds)
        {
            _syncJobRepository = syncJobRepository;
            _runtimeRetrievalService = runtimeRetrievalService;
            _telemetryClient = telemetryClient;
            START_TIME_DELAY_MINUTES = startTimeDelayMinutes;
            BUFFER_SECONDS = delayBetweenSyncsSeconds;
        }

        public async Task<List<SchedulerSyncJob>> GetAllSyncJobs(bool includeFutureStartDates = false)
        {
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

            return schedulerSyncJobs;
        }

        public async Task UpdateSyncJobs(List<SchedulerSyncJob> updatedSyncJobs)
        {
            await _syncJobRepository.UpdateSyncJobsAsync(updatedSyncJobs);
        }

        public Task<List<SchedulerSyncJob>> ResetJobStartTimes(List<SchedulerSyncJob> schedulerSyncJobs, DateTime newStartTime, bool includeFutureStartDates = false)
        {
            List<SchedulerSyncJob> updatedSyncJobs = schedulerSyncJobs.Select(job => {
                if (includeFutureStartDates || job.StartDate.CompareTo(newStartTime) < 0)
                    job.StartDate = newStartTime;
                return job;
            }).ToList();

            return Task.FromResult(updatedSyncJobs);
        }

        public async Task<List<SchedulerSyncJob>> DistributeJobStartTimes(List<SchedulerSyncJob> schedulerSyncJobs)
        {
            // Get all runtimes for destination groups
            List<Guid> groupIds = schedulerSyncJobs.ConvertAll(job => job.TargetOfficeGroupId);
            Dictionary<Guid, double> runtimeMap = await _runtimeRetrievalService.GetRuntimes(groupIds);

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

                List<SchedulerSyncJob> updatedJobsForPeriod = DistributeJobStartTimesForPeriod(jobsForPeriod, period, runtimeMap);
                updatedJobs.AddRange(updatedJobsForPeriod);
            }

            return updatedJobs;
        }

        private List<SchedulerSyncJob> DistributeJobStartTimesForPeriod(
            List<SchedulerSyncJob> schedulerSyncJobs, 
            int period,
            Dictionary<Guid, double> runtimeMap)
        {
            HashSet<Guid> groupIdsForPeriod = new HashSet<Guid>(schedulerSyncJobs.ConvertAll(job => job.TargetOfficeGroupId));
            runtimeMap = new Dictionary<Guid, double>(runtimeMap.Where(entry => groupIdsForPeriod.Contains(entry.Key)));

            // Sort sync jobs by Status, LastRunTime
            schedulerSyncJobs.Sort();

            double totalTime = ((IEnumerable<double>)runtimeMap.Values).Sum();

            int concurrencyNumber = (int) Math.Ceiling(totalTime / (period * 60 * 60));
            Console.WriteLine(@"Number of threads: " + concurrencyNumber);

            List<DateTime> jobThreads = new List<DateTime>(concurrencyNumber);
            DateTime startTime = DateTime.UtcNow.AddMinutes(START_TIME_DELAY_MINUTES);
            for(int index = 0; index < concurrencyNumber; index++)
            {
                jobThreads.Add(startTime);
            }

            DateTime latestTime = startTime.AddHours(period);
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
