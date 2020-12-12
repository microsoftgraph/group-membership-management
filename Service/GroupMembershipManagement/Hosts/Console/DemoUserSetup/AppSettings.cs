using Microsoft.Extensions.Configuration;

namespace DemoUserSetup
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
