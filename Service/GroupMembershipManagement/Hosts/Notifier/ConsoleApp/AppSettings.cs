// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration;
using Repositories.Contracts.InjectConfig;

namespace Notifier
{
    public class AppSettings : INotifierConfig
    {
        public string LogAnalyticsCustomerId { get; set; }
        public string LogAnalyticsPrimarySharedKey { get; set; }
        public string APPINSIGHTS_INSTRUMENTATIONKEY { get; set; }
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