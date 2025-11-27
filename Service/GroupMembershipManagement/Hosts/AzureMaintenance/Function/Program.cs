// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.EntityFramework;
using Repositories.GraphGroups;
using Repositories.NotificationsRepository;
using Repositories.ServiceBusQueue;
using Services;
using Services.Contracts;
using System;

namespace Hosts.AzureMaintenance
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureAppConfiguration((context, config) =>
                {
                    var settings = config.Build();
                    var appConfigEndpoint = CommonServices.GetValueOrThrowBase(settings, "appConfigurationEndpoint");

                    config.AddAzureAppConfiguration(options =>
                    {
                        DefaultAzureCredential credential = new DefaultAzureCredential();
                        options.Connect(new Uri(appConfigEndpoint), credential).UseFeatureFlags();
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    var functionName = "AzureMaintenance";
                    var dryRunSettingName = string.Empty;
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.AddScoped<IDatabasePurgedSyncJobsRepository, DatabasePurgedSyncJobsRepository>();

                    services.AddOptions<HandleInactiveJobsConfig>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        settings.HandleInactiveJobsEnabled = GetBoolSetting(configuration, "AzureMaintenance:HandleInactiveJobsEnabled", false);
                        settings.NumberOfDaysBeforePurging = GetIntSetting(configuration, "AzureMaintenance:NumberOfDaysBeforePurging", 30);
                        settings.NumberOfDaysBeforePurgingToSendWarning = GetIntSetting(configuration, "AzureMaintenance:NumberOfDaysBeforePurgingToSendWarning", 7);
                        settings.NumberOfDaysBeforeDeletion = GetIntSetting(configuration, "AzureMaintenance:NumberOfDaysBeforeDeletion", 35);
                    });
                    services.AddSingleton<IHandleInactiveJobsConfig>(services =>
                    {
                        return new HandleInactiveJobsConfig(
                            services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.HandleInactiveJobsEnabled,
                            services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.NumberOfDaysBeforePurging,
                            services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.NumberOfDaysBeforePurgingToSendWarning,
                            services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.NumberOfDaysBeforeDeletion);
                    });

                    services.AddOptions<ThresholdNotificationConfig>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        settings.IsThresholdNotificationEnabled = GetBoolSetting(configuration, "ThresholdNotification:IsThresholdNotificationEnabled", false);
                    });
                    services.AddSingleton<IThresholdNotificationConfig>(services =>
                    {
                        return new ThresholdNotificationConfig(
                            services.GetService<IOptions<ThresholdNotificationConfig>>().Value.IsThresholdNotificationEnabled);
                    });

                    services.AddSingleton<INotificationRepository, NotificationRepository>();

                    services
                        .AddGraphAPIClient()
                        .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

                    services.AddScoped<IAzureMaintenanceService>(services =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(notificationsQueue);
                        var notificationsQueueRepository = new ServiceBusQueueRepository(sender);

                        return new AzureMaintenanceService(
                            services.GetService<IDatabaseSyncJobsRepository>(),
                            services.GetService<IDatabaseGroupsRepository>(),
                            services.GetService<IDatabaseChannelsRepository>(),
                            services.GetService<IDatabasePurgedSyncJobsRepository>(),
                            services.GetService<IGraphGroupRepository>(),
                            services.GetService<IHandleInactiveJobsConfig>(),
                            services.GetService<INotificationRepository>(),
                            notificationsQueueRepository,
                            services.GetService<ILoggingRepository>());
                    });
                }).Build();

            host.Run();
        }

        private static bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            return checkParse ? value : defaultValue;
        }

        private static int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var checkParse = int.TryParse(configuration[settingName], out int value);
            return checkParse ? value : defaultValue;
        }
    }
}
