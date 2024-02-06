// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration;

namespace Repositories.DataFactory
{    
    public class AppSettings
    {
        public string ResourceGroup { get; set; }
        public string SubscriptionId { get; set; }

        public static AppSettings LoadAppSettings()
        {
            IConfigurationRoot configRoot = new ConfigurationBuilder()
                .AddJsonFile("Settings.json")
                .Build();
            return configRoot.Get<AppSettings>();
        }
    }
}
