// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.AzureMaintenance;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.EntityFramework;
using Repositories.GraphGroups;
using Repositories.NotificationsRepository;
using Services;
using Services.Contracts;

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.AzureMaintenance
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(AzureMaintenance);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddScoped<IDatabasePurgedSyncJobsRepository, DatabasePurgedSyncJobsRepository>();

            builder.Services.AddOptions<HandleInactiveJobsConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.HandleInactiveJobsEnabled = GetBoolSetting(configuration, "AzureMaintenance:HandleInactiveJobsEnabled", false);
                settings.NumberOfDaysBeforeDeletion = GetIntSetting(configuration, "AzureMaintenance:NumberOfDaysBeforeDeletion", 0);
            });
            builder.Services.AddSingleton<IHandleInactiveJobsConfig>(services =>
            {
                return new HandleInactiveJobsConfig(
                    services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.HandleInactiveJobsEnabled,
                    services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.NumberOfDaysBeforeDeletion);
            });

            builder.Services.AddOptions<ThresholdNotificationConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.IsThresholdNotificationEnabled = GetBoolSetting(configuration, "ThresholdNotification:IsThresholdNotificationEnabled", false);
            });
            builder.Services.AddSingleton<IThresholdNotificationConfig>(services =>
            {
                return new ThresholdNotificationConfig(
                    services.GetService<IOptions<ThresholdNotificationConfig>>().Value.IsThresholdNotificationEnabled);
            });

            builder.Services.AddOptions<NotificationRepoCredentials<NotificationRepository>>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ConnectionString = configuration.GetValue<string>("jobsStorageAccountConnectionString");
                settings.TableName = configuration.GetValue<string>("notificationsTableName");
            });
            builder.Services.AddSingleton<INotificationRepository, NotificationRepository>();


            builder.Services.AddSingleton<IKeyVaultSecret<IAzureMaintenanceService>>(services => new KeyVaultSecret<IAzureMaintenanceService>(services.GetService<IOptions<GraphCredentials>>().Value.ClientId))
            .AddGraphAPIClient()
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

            builder.Services.AddScoped<IAzureMaintenanceService>(services =>
            {
                return new AzureMaintenanceService(services.GetService<IDatabaseSyncJobsRepository>(),
                    services.GetService<IDatabasePurgedSyncJobsRepository>(),
                    services.GetService<IGraphGroupRepository>(),
                    services.GetService<IEmailSenderRecipient>(),
                    services.GetService<IMailRepository>(),
                    services.GetService<IHandleInactiveJobsConfig>(),
                    services.GetService<INotificationRepository>());
            });
        }

        private bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            if (checkParse)
                return value;
            return defaultValue;
        }

        private int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var checkParse = int.TryParse(configuration[settingName], out int value);
            if (checkParse)
                return value;
            return defaultValue;
        }
    }
}
