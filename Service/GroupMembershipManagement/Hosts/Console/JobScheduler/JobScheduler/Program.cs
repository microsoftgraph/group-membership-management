// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Services;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Repositories.SyncJobsRepository;
using Services.Entities;
using Newtonsoft.Json;
using Repositories.Logging;
using Repositories.Contracts.InjectConfig;
using DIConcreteTypes;

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
            var logAnalyticsSecret = new LogAnalyticsSecret<LoggingRepository>(appSettings.LogAnalyticsCustomerId, appSettings.LogAnalyticsPrimarySharedKey, "JobScheduler");
            var _loggingRepository = new LoggingRepository(logAnalyticsSecret);
            var _syncJobRepository = new SyncJobRepository(appSettings.JobsTableConnectionString, appSettings.JobsTableName, _loggingRepository);

            var defaultRuntime = appSettings.DefaultRuntime;
            var _runtimeRetrievalService = new DefaultRuntimeRetrievalService(defaultRuntime);

            var telemetryConfiguration = new TelemetryConfiguration();
            telemetryConfiguration.InstrumentationKey = appSettings.AppInsightsInstrumentationKey;
            telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());

            _telemetryClient = new TelemetryClient(telemetryConfiguration);

            var startTimeDelayMinutes = appSettings.StartTimeDelayMinutes;
            var delayBetweenSyncsSeconds = appSettings.DelayBetweenSyncsSeconds;

            _jobSchedulingService = new JobSchedulingService(
                startTimeDelayMinutes,
                delayBetweenSyncsSeconds,
                _syncJobRepository,
                _runtimeRetrievalService,
                _loggingRepository
            );

            var jobSchedulerConfig = JsonConvert.DeserializeObject<JobSchedulerConfiguration>(appSettings.JobSchedulerConfig);

            // Function running
            bool updateFutureJobsToo = jobSchedulerConfig.IncludeFutureJobs;
            bool resetJobs = jobSchedulerConfig.ResetJobs;
            bool distributeJobs = jobSchedulerConfig.DistributeJobs;

            Console.Write("Getting jobs...");
            List<SchedulerSyncJob> jobs = await _jobSchedulingService.GetAllSyncJobsAsync(updateFutureJobsToo);
            Console.WriteLine(" jobs retrieved!");

            if (resetJobs)
            {
                Console.Write("Resetting jobs...");
                jobs = _jobSchedulingService.ResetJobStartTimes(jobs, DateTime.UtcNow.AddDays(-1), updateFutureJobsToo);
                await _jobSchedulingService.UpdateSyncJobsAsync(jobs);
                Console.WriteLine(" jobs reset!");
            }
            if (distributeJobs)
            {
                List<SchedulerSyncJob> updatedJobs = await _jobSchedulingService.DistributeJobStartTimesAsync(jobs);
                await _jobSchedulingService.UpdateSyncJobsAsync(updatedJobs);
            }

            Console.WriteLine("Done");
        }
    }
}