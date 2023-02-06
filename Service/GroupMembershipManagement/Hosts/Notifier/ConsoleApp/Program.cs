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

namespace Notifier
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var appSettings = AppSettings.LoadAppSettings();

            // Injections
            var logAnalyticsSecret = new LogAnalyticsSecret<LoggingRepository>(appSettings.LogAnalyticsCustomerId, appSettings.LogAnalyticsPrimarySharedKey, "Notifier");
            var appConfigVerbosity = new AppConfigVerbosity { Verbosity = VerbosityLevel.INFO };
            var loggingRepository = new LoggingRepository(logAnalyticsSecret, appConfigVerbosity);
            var syncJobRepository = new SyncJobRepository(appSettings.JobsStorageAccountConnectionString, appSettings.JobsTableName, loggingRepository);


            var telemetryConfiguration = new TelemetryConfiguration();
            telemetryConfiguration.InstrumentationKey = appSettings.APPINSIGHTS_INSTRUMENTATIONKEY;
            telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());

            var notifierConfig = new NotifierConfig(
                appSettings.WorkspaceId
                );

            var logsQueryClient = new LogsQueryClient(new DefaultAzureCredential());
            var runtimeRetrievalService = new DefaultRuntimeRetrievalService(notifierConfig, logsQueryClient);

            IApplicationService applicationService = new ApplicationService(notifierConfig, loggingRepository);

            // Call the Notifier ApplicationService
            await applicationService.RunAsync();
        }
    }
}