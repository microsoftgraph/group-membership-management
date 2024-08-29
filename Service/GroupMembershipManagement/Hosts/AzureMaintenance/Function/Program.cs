// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Hosting;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services;
using Services.Contracts;
using System;
using Azure.Identity;
using Repositories.EntityFramework;
using Repositories.NotificationsRepository;

namespace Hosts.AzureMaintenance
{

    public class Program
    {
        public static void Main (string[] args)
        {
            var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureAppConfiguration((context, config) =>
            {
                var settings = config.Build();
                var appConfigEndpoint = CommonServices.GetValueOrThrowBase("appConfigurationEndpoint");

                config.AddAzureAppConfiguration(options =>
                {
                    options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential())
                           .UseFeatureFlags();
                });
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                var SCHEMA_DIRECTORY = "JsonSchemas";
                var functionName = "AzureMaintenance";
                var dryRunSettingName = string.Empty;
                var rootPath = context.HostingEnvironment.ContentRootPath;
                CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
                           services.AddScoped<IDatabasePurgedSyncJobsRepository, DatabasePurgedSyncJobsRepository>();

            services.AddOptions<HandleInactiveJobsConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.HandleInactiveJobsEnabled = CommonServices.GetBoolSettingBase(configuration, "AzureMaintenance:HandleInactiveJobsEnabled", false);
                settings.NumberOfDaysBeforeDeletion = CommonServices.GetIntSettingBase(configuration, "AzureMaintenance:NumberOfDaysBeforeDeletion", 0);
            });
            services.AddSingleton<IHandleInactiveJobsConfig>(services =>
            {
                return new HandleInactiveJobsConfig(
                    services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.HandleInactiveJobsEnabled,
                    services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.NumberOfDaysBeforeDeletion);
            });

            services.AddOptions<ThresholdNotificationConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.IsThresholdNotificationEnabled = CommonServices.GetBoolSettingBase(configuration, "ThresholdNotification:IsThresholdNotificationEnabled", false);
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

                return new AzureMaintenanceService(services.GetService<IDatabaseSyncJobsRepository>(),
                    services.GetService<IDatabasePurgedSyncJobsRepository>(),
                    services.GetService<IGraphGroupRepository>(),
                    services.GetService<IHandleInactiveJobsConfig>(),
                    services.GetService<INotificationRepository>(),
                    notificationsQueueRepository,
                    services.GetService<ILoggingRepository>());
        });
            })
    .       Build();
            host.Run();
        }
    }
}
