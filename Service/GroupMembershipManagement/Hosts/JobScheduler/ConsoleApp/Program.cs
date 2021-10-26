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
using Repositories.Contracts.InjectConfig;

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
            var syncJobRepository = new SyncJobRepository(appSettings.JobsStorageAccountConnectionString, appSettings.JobsTableName, loggingRepository);


            var telemetryConfiguration = new TelemetryConfiguration();
            telemetryConfiguration.InstrumentationKey = appSettings.APPINSIGHTS_INSTRUMENTATIONKEY;
            telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());

            IJobSchedulerConfig jobSchedulerConfig = new JobSchedulerConfig(
                appSettings.ResetJobs,
                appSettings.DaysToAddForReset,
                appSettings.DistributeJobs,
                appSettings.IncludeFutureJobs,
                appSettings.StartTimeDelayMinutes,
                appSettings.DelayBetweenSyncsSeconds,
                appSettings.DefaultRuntime);

            var runtimeRetrievalService = new DefaultRuntimeRetrievalService(jobSchedulerConfig.DefaultRuntimeSeconds);

            IJobSchedulingService jobSchedulingService = new JobSchedulingService(
                jobSchedulerConfig,
                syncJobRepository,
                runtimeRetrievalService,
                loggingRepository
            );

            IApplicationService applicationService = new ApplicationService(jobSchedulingService, jobSchedulerConfig, loggingRepository);

            // Call the JobScheduler ApplicationService
            await applicationService.RunAsync();
        }
    }
}