// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Configuration;

namespace Console
{
    public class AppSettings
    {
        public string ClientId { get; set; }
        public string TenantId { get; set; }
        public string TenantName { get; set; }
        public int UserCount { get; set; }

        public static AppSettings LoadAppSettings()
        {
            IConfigurationRoot configRoot = new ConfigurationBuilder()
                .AddJsonFile("Settings.json")
                .Build();
            return configRoot.Get<AppSettings>();
        }
    }
}
