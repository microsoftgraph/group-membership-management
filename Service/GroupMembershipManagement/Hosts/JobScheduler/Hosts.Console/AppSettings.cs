// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration;

namespace JobScheduler
{
    public class AppSettings
    {
        public string LogAnalyticsCustomerId { get; set; }
        public string LogAnalyticsPrimarySharedKey { get; set; }
        public string JobsTableConnectionString { get; set; }
        public string JobsTableName { get; set; }
        public string AppInsightsInstrumentationKey { get; set; }
        public int DefaultRuntime { get; set; }
        public string JobSchedulerConfig { get; set; }
        public int DaysToAddForReset { get; set; }
        public int StartTimeDelayMinutes { get; set; }
        public int DelayBetweenSyncsSeconds { get; set; }

        public static AppSettings LoadAppSettings()
        {
            IConfigurationRoot configRoot = new ConfigurationBuilder()
                .AddJsonFile("Settings.json")
                .Build();

            return configRoot.Get<AppSettings>();
        }
    }
}