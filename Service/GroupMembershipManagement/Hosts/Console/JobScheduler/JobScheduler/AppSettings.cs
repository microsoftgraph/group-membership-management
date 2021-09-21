using Microsoft.Extensions.Configuration;

namespace JobScheduler
{
    public class AppSettings
    {
        public string JobsTableConnectionString { get; set; }
        public string JobsTableName { get; set; }
        public string APPINSIGHTS_INSTRUMENTATIONKEY { get; set; }
        public int DefaultRuntime { get; set; }
        public string JobSchedulerConfig { get; set; }
        public int startTimeDelayMinutes { get; set; }
        public int delayBetweenSyncsSeconds { get; set; }

        public static AppSettings LoadAppSettings()
        {
            IConfigurationRoot configRoot = new ConfigurationBuilder()
                .AddJsonFile("Settings.json")
                .Build();

            return configRoot.Get<AppSettings>();
        }
    }
}