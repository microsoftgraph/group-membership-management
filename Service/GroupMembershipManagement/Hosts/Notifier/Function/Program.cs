// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.EntityFramework;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services.Contracts.Notifications;
using Services.Notifications;
using Services.Notifier;
using Services.Notifier.Contracts;
using System;

namespace Hosts.Notifier
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
                        DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
                        options.Connect(new Uri(appConfigEndpoint), credential).UseFeatureFlags();
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    var functionName = "Notifier";

                    CommonServices.ConfigureCommonServices(
                        services,
                        configuration,
                        functionName,
                        dryRunSettingName: string.Empty,
                        rootPath);

                    services.ConfigureFunctionsApplicationInsights();

                    services.AddOptions<HandleInactiveJobsConfig>().Configure<IConfiguration>((settings, config) =>
                    {
                        settings.HandleInactiveJobsEnabled = CommonServices.GetBoolSettingBase(config, "AzureMaintenance:HandleInactiveJobsEnabled", false);
                        settings.NumberOfDaysBeforePurging = CommonServices.GetIntSettingBase(config, "AzureMaintenance:NumberOfDaysBeforePurging", 30);
                        settings.NumberOfDaysBeforePurgingToSendWarning = CommonServices.GetIntSettingBase(config, "AzureMaintenance:NumberOfDaysBeforePurgingToSendWarning", 7);
                        settings.NumberOfDaysBeforeDeletion = CommonServices.GetIntSettingBase(config, "AzureMaintenance:NumberOfDaysBeforeDeletion", 35);
                        settings.JobHistoryRetentionDays = CommonServices.GetIntSettingBase(config, "AzureMaintenance:JobHistoryRetentionDays", 30);
                    });

                    services.AddSingleton<IHandleInactiveJobsConfig>(sp =>
                    {
                        var options = sp.GetRequiredService<IOptions<HandleInactiveJobsConfig>>().Value;
                        return new HandleInactiveJobsConfig(
                            options.HandleInactiveJobsEnabled,
                            options.NumberOfDaysBeforePurging,
                            options.NumberOfDaysBeforePurgingToSendWarning,
                            options.NumberOfDaysBeforeDeletion,
                            options.JobHistoryRetentionDays);
                    });

                    services.AddOptions<ThresholdNotificationServiceConfig>().Configure<IConfiguration>((settings, config) =>
                    {
                        settings.ActionableEmailProviderId = config.GetValue<Guid>("actionableEmailProviderId");
                        settings.ApiHostname = config.GetValue<string>("apiHostname");
                    });

                    services.AddOptions<ThresholdConfig>().Configure<IConfiguration>((settings, config) =>
                    {
                        settings.MaximumNumberOfThresholdRecipients = CommonServices.GetIntSettingBase(config, "MaximumNumberOfThresholdRecipients", 10);
                        settings.NumberOfThresholdViolationsToNotify = CommonServices.GetIntSettingBase(config, "NumberOfThresholdViolationsToNotify", 3);
                        settings.NumberOfThresholdViolationsFollowUps = CommonServices.GetIntSettingBase(config, "NumberOfThresholdViolationsFollowUps", 3);
                        settings.NumberOfThresholdViolationsToDisableJob = CommonServices.GetIntSettingBase(config, "NumberOfThresholdViolationsToDisableJob", 10);
                    });

                    services.AddSingleton<IThresholdConfig>(sp =>
                    {
                        var options = sp.GetRequiredService<IOptions<ThresholdConfig>>().Value;
                        return new ThresholdConfig(
                            options.MaximumNumberOfThresholdRecipients,
                            options.NumberOfThresholdViolationsToNotify,
                            options.NumberOfThresholdViolationsFollowUps,
                            options.NumberOfThresholdViolationsToDisableJob);
                    });

                    services
                        .AddGraphAPIClient()
                        .AddLocalization(options => options.ResourcesPath = "Resources")
                        .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
                        .AddScoped<IDatabaseSyncJobsRepository, DatabaseSyncJobsRepository>()
                        .AddScoped<INotifierService, NotifierService>();

                    services.AddSingleton<IServiceBusQueueRepository>(sp =>
                    {
                        var config = sp.GetRequiredService<IConfiguration>();
                        var failedNotificationsQueue = config["serviceBusFailedNotificationsQueue"];
                        var client = sp.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(failedNotificationsQueue);
                        return new ServiceBusQueueRepository(sender);
                    });

                    services.AddSingleton<IThresholdNotificationConfig>(_ => new ThresholdNotificationConfig(true));
                    services.AddScoped<IThresholdNotificationService, ThresholdNotificationService>();
                    services.AddHttpClient();
                })
                .Build();

            host.Run();
        }
    }
}
