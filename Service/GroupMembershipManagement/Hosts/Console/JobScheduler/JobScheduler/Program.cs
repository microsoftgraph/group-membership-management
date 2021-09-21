using Entities;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Services;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Repositories.SyncJobsRepository;
using Services.Entities;
using Newtonsoft.Json;

namespace JobScheduler
{
    internal static class Program
    {
        private static JobSchedulingService _jobSchedulingService;
        private static TelemetryClient _telemetryClient;

        private static async Task Main(string[] args)
        {
            var appSettings = AppSettings.LoadAppSettings();

            // Injections
            var _syncJobRepository = new SyncJobRepository(appSettings.JobsTableConnectionString, appSettings.JobsTableName, new MockLoggingRepository());

            int defaultRuntime = appSettings.DefaultRuntime;
            var _runtimeRetrievalService = new DefaultRuntimeRetrievalService(defaultRuntime);

            var telemetryConfiguration = new TelemetryConfiguration();
            telemetryConfiguration.InstrumentationKey = appSettings.APPINSIGHTS_INSTRUMENTATIONKEY;
            telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());

            _telemetryClient = new TelemetryClient(telemetryConfiguration);

            var startTimeDelayMinutes = appSettings.startTimeDelayMinutes;
            var delayBetweenSyncsSeconds = appSettings.delayBetweenSyncsSeconds;

            _jobSchedulingService = new JobSchedulingService(
                _syncJobRepository,
                _runtimeRetrievalService,
                _telemetryClient,
                startTimeDelayMinutes,
                delayBetweenSyncsSeconds
            );

            var jobSchedulerConfig = JsonConvert.DeserializeObject<JobSchedulerConfiguration>(appSettings.JobSchedulerConfig);

            // Function running
            bool updateFutureJobsToo = jobSchedulerConfig.IncludeFutureJobs;
            bool resetJobs = jobSchedulerConfig.ResetJobs;
            bool distributeJobs = jobSchedulerConfig.DistributeJobs;

            Console.Write("Getting jobs...");
            List<SchedulerSyncJob> jobs = await _jobSchedulingService.GetAllSyncJobs(updateFutureJobsToo);
            Console.WriteLine(" jobs retrieved!");

            if (resetJobs)
            {
                Console.Write("Resetting jobs...");
                jobs = await _jobSchedulingService.ResetJobStartTimes(jobs, DateTime.UtcNow.AddDays(-1), updateFutureJobsToo);
                await _jobSchedulingService.UpdateSyncJobs(jobs);
                Console.WriteLine(" jobs reset!");
            }
            if (distributeJobs)
            {
                List<SchedulerSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimes(jobs);
                await _jobSchedulingService.UpdateSyncJobs(updatedJobs);
            }

            Console.WriteLine("Done");
        }
    }
}