// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;
using Services;
using Microsoft.ApplicationInsights.Extensibility;
using Repositories.Logging;
using DIConcreteTypes;
using Services.Contracts;
using Repositories.Contracts;
using Repositories.EntityFramework;
using Repositories.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;

namespace JobScheduler
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var appSettings = AppSettings.LoadAppSettings();

            var writeContext = new GMMContext(new DbContextOptionsBuilder<GMMContext>()
                                        .UseSqlServer(appSettings.SQLDatabaseConnectionString)
                                        .Options);
            var readContext = new GMMReadContext(new DbContextOptionsBuilder<GMMContext>()
                                        .UseSqlServer(appSettings.ReplicaSqlServerConnectionString)
                                        .Options);

            // Injections
            var logAnalyticsSecret = new LogAnalyticsSecret<LoggingRepository>(appSettings.LogAnalyticsCustomerId, appSettings.LogAnalyticsPrimarySharedKey, "JobScheduler");
            var appConfigVerbosity = new AppConfigVerbosity { Verbosity = VerbosityLevel.INFO };
            var loggingRepository = new LoggingRepository(logAnalyticsSecret);
            var syncJobRepository = new DatabaseSyncJobsRepository(writeContext, readContext);


            var telemetryConfiguration = new TelemetryConfiguration();
            telemetryConfiguration.InstrumentationKey = appSettings.APPINSIGHTS_INSTRUMENTATIONKEY;
            telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());

            var jobSchedulerConfig = new JobSchedulerConfig(
                appSettings.ResetJobs,
                appSettings.DaysToAddForReset,
                appSettings.DistributeJobs,
                appSettings.StartTimeDelayMinutes,
                appSettings.DelayBetweenSyncsSeconds,
                appSettings.DefaultRuntimeSeconds,
                appSettings.GetRunTimeFromLogs,
                appSettings.RunTimeMetric,
                appSettings.RunTimeQuery,
                appSettings.RunTimeRangeInDays,
                appSettings.WorkspaceId
                );

            var runtimeRetrievalService = new DefaultRuntimeRetrievalService(defaultRuntimeSeconds: 60);
            var jobSchedulingService = new JobSchedulingService(
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