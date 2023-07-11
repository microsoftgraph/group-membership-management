// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;
using Services;
using Microsoft.ApplicationInsights.Extensibility;
using Repositories.Logging;
using DIConcreteTypes;
using Services.Contracts;
using Repositories.Contracts;
using Azure.Identity;
using Azure.Monitor.Query;
using Repositories.EntityFramework;
using Repositories.EntityFramework.Contexts;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.EntityFrameworkCore;

namespace JobScheduler
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var appSettings = AppSettings.LoadAppSettings();

            var gmmContext = new GMMContext(new DbContextOptionsBuilder<GMMContext>()
                                        .UseSqlServer(appSettings.SQLDatabaseConnectionString)
                                        .Options);

            // Injections
            var logAnalyticsSecret = new LogAnalyticsSecret<LoggingRepository>(appSettings.LogAnalyticsCustomerId, appSettings.LogAnalyticsPrimarySharedKey, "JobScheduler");
            var appConfigVerbosity = new AppConfigVerbosity { Verbosity = VerbosityLevel.INFO };
            var loggingRepository = new LoggingRepository(logAnalyticsSecret);
            var syncJobRepository = new DatabaseSyncJobsRepository(gmmContext);


            var telemetryConfiguration = new TelemetryConfiguration();
            telemetryConfiguration.InstrumentationKey = appSettings.APPINSIGHTS_INSTRUMENTATIONKEY;
            telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());

            var jobSchedulerConfig = new JobSchedulerConfig(
                appSettings.ResetJobs,
                appSettings.DaysToAddForReset,
                appSettings.DistributeJobs,
                appSettings.IncludeFutureJobs,
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