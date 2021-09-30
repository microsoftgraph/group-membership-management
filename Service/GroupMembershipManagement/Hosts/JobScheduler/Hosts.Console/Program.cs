// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using Services;
using Microsoft.ApplicationInsights.Extensibility;
using Repositories.SyncJobsRepository;
using Services.Entities;
using Newtonsoft.Json;
using Repositories.Logging;
using DIConcreteTypes;
using Services.Contracts;

namespace JobScheduler
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var appSettings = AppSettings.LoadAppSettings();

            // Injections
            var logAnalyticsSecret = new LogAnalyticsSecret<LoggingRepository>(appSettings.LogAnalyticsCustomerId, appSettings.LogAnalyticsPrimarySharedKey, "JobScheduler");
            var loggingRepository = new LoggingRepository(logAnalyticsSecret);
            var syncJobRepository = new SyncJobRepository(appSettings.JobsTableConnectionString, appSettings.JobsTableName, loggingRepository);

            var defaultRuntime = appSettings.DefaultRuntime;
            var runtimeRetrievalService = new DefaultRuntimeRetrievalService(defaultRuntime);

            var telemetryConfiguration = new TelemetryConfiguration();
            telemetryConfiguration.InstrumentationKey = appSettings.AppInsightsInstrumentationKey;
            telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());

            var startTimeDelayMinutes = appSettings.StartTimeDelayMinutes;
            var delayBetweenSyncsSeconds = appSettings.DelayBetweenSyncsSeconds;

            IJobSchedulingService jobSchedulingService = new JobSchedulingService(
                startTimeDelayMinutes,
                delayBetweenSyncsSeconds,
                syncJobRepository,
                runtimeRetrievalService,
                loggingRepository
            );

            IApplicationService applicationService = new ApplicationService(jobSchedulingService);

            // Get JobSchedulerConfiguration values
            var jobSchedulerConfig = JsonConvert.DeserializeObject<JobSchedulerConfiguration>(appSettings.JobSchedulerConfig);
            bool resetJobs = jobSchedulerConfig.ResetJobs;
            bool distributeJobs = jobSchedulerConfig.DistributeJobs;
            bool includeFutureJobs = jobSchedulerConfig.IncludeFutureJobs;

            // Call the JobSchedulerService
            await applicationService.RunAsync(resetJobs, distributeJobs, includeFutureJobs, appSettings.DaysToAddForReset);
        }
    }
}