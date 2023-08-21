// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration;
using Repositories.Contracts.InjectConfig;

namespace JobScheduler
{
    public class AppSettings : IJobSchedulerConfig
    {
        public string LogAnalyticsCustomerId { get; set; }
        public string LogAnalyticsPrimarySharedKey { get; set; }
        public string JobsStorageAccountConnectionString { get; set; }
        public string SQLDatabaseConnectionString { get; set; }
        public string APPINSIGHTS_INSTRUMENTATIONKEY { get; set; }

        public bool ResetJobs { get; set; }
        public int DaysToAddForReset { get; set; }
        public bool DistributeJobs { get; set; }
        public bool IncludeFutureJobs { get; set; }
        public int StartTimeDelayMinutes { get; set; }
        public int DelayBetweenSyncsSeconds { get; set; }
        public int DefaultRuntimeSeconds { get; set; }
        public bool GetRunTimeFromLogs { get; set; }
        public string RunTimeMetric { get; set; }
        public string RunTimeQuery { get; set; }
        public int RunTimeRangeInDays { get; set; }
        public string WorkspaceId { get; set; }

        public static AppSettings LoadAppSettings()
        {
            IConfigurationRoot configRoot = new ConfigurationBuilder()
                .AddJsonFile("Settings.json")
                .Build();

            return configRoot.Get<AppSettings>();
        }
    }
}