// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Hosting;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services.Contracts.Notifications;
using Services.Notifications;
using Services.Notifier;
using Services.Notifier.Contracts;
using System;
using Hosts.FunctionBase;
using Azure.Identity;
using System.Runtime.CompilerServices;

namespace Hosts.Notifier
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
                var functionName = "Notifier";
                var dryRunSettingName = string.Empty;
                var rootPath = context.HostingEnvironment.ContentRootPath;
                CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
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

                services.AddGraphAPIClient()
                .AddLocalization(options =>
                {
                    options.ResourcesPath = "Resources";
                })
                .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
                .AddScoped<INotifierService, NotifierService>();

                services.AddOptions<ThresholdNotificationServiceConfig>().Configure<IConfiguration>((settings, configuration) =>
                {
                    settings.ActionableEmailProviderId = configuration.GetValue<Guid>("actionableEmailProviderId");
                    settings.ApiHostname = configuration.GetValue<string>("apiHostname");
                });
                services.AddOptions<ThresholdConfig>().Configure<IConfiguration>((settings, configuration) =>
                {
                    settings.MaximumNumberOfThresholdRecipients = CommonServices.GetIntSettingBase(configuration, "MaximumNumberOfThresholdRecipients", 10);
                    settings.NumberOfThresholdViolationsToNotify = CommonServices.GetIntSettingBase(configuration, "NumberOfThresholdViolationsToNotify", 3);
                    settings.NumberOfThresholdViolationsFollowUps = CommonServices.GetIntSettingBase(configuration, "NumberOfThresholdViolationsFollowUps", 3);
                    settings.NumberOfThresholdViolationsToDisableJob = CommonServices.GetIntSettingBase(configuration, "NumberOfThresholdViolationsToDisableJob", 10);
                });
                services.AddSingleton<IThresholdConfig>(services =>
                {
                    return new ThresholdConfig
                        (
                            services.GetService<IOptions<ThresholdConfig>>().Value.MaximumNumberOfThresholdRecipients,
                            services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToNotify,
                            services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsFollowUps,
                            services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToDisableJob
                        );
                });
                services.AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                {
                    var configuration = services.GetRequiredService<IConfiguration>();
                    var failedNotificationsQueue = configuration["serviceBusFailedNotificationsQueue"];
                    var client = services.GetRequiredService<ServiceBusClient>();
                    var sender = client.CreateSender(failedNotificationsQueue);
                    return new ServiceBusQueueRepository(sender);
                });
                services.AddScoped<IThresholdNotificationConfig>((sp) =>
                {
                    return new ThresholdNotificationConfig(true);
                });
                services.AddScoped<IThresholdNotificationService, ThresholdNotificationService>();
                services.AddHttpClient();

            })
            .Build();
                
        host.Run();
        }
    }
}