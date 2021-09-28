// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Services;
using Microsoft.ApplicationInsights.Extensibility;
using Repositories.SyncJobsRepository;
using Services.Entities;
using Newtonsoft.Json;
using Repositories.Logging;
using DIConcreteTypes;

namespace JobScheduler
{
    internal static class Program
    {
        private static JobSchedulingService _jobSchedulingService;

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

            // Calling the JobSchedulerService
            bool updateFutureJobsToo = jobSchedulerConfig.IncludeFutureJobs;
            bool resetJobs = jobSchedulerConfig.ResetJobs;
            bool distributeJobs = jobSchedulerConfig.DistributeJobs;

            Console.Write("Getting jobs...");
            List<SchedulerSyncJob> jobs = await _jobSchedulingService.GetAllSyncJobsAsync(updateFutureJobsToo);
            Console.WriteLine(" jobs retrieved!");

            Console.Write("Scheduling jobs...");
            await _jobSchedulingService.ScheduleJobs(jobs, resetJobs, distributeJobs, updateFutureJobsToo);
            Console.WriteLine("jobs scheduled!");
        }
    }
}