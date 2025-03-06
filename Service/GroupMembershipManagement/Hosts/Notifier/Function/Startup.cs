// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Hosts.Notifier;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
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

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.Notifier
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(Notifier);
        protected override string DryRunSettingName => string.Empty;
        protected string NotifierConfigSettingName => "Notifier:NotifierConfiguration";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

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

            builder.Services.AddGraphAPIClient()
            .AddLocalization(options =>
            {
                options.ResourcesPath = "Resources";
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddScoped<INotifierService, NotifierService>();

            builder.Services.AddOptions<ThresholdNotificationServiceConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ActionableEmailProviderId = configuration.GetValue<Guid>("actionableEmailProviderId");
                settings.ApiHostname = configuration.GetValue<string>("apiHostname");
            });
            builder.Services.AddOptions<ThresholdConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.MaximumNumberOfThresholdRecipients = GetIntSetting(configuration, "MaximumNumberOfThresholdRecipients", 10);
                settings.NumberOfThresholdViolationsToNotify = GetIntSetting(configuration, "NumberOfThresholdViolationsToNotify", 3);
                settings.NumberOfThresholdViolationsFollowUps = GetIntSetting(configuration, "NumberOfThresholdViolationsFollowUps", 3);
                settings.NumberOfThresholdViolationsToDisableJob = GetIntSetting(configuration, "NumberOfThresholdViolationsToDisableJob", 10);
            });
            builder.Services.AddSingleton<IThresholdConfig>(services =>
            {
                return new ThresholdConfig
                    (
                        services.GetService<IOptions<ThresholdConfig>>().Value.MaximumNumberOfThresholdRecipients,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToNotify,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsFollowUps,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToDisableJob
                    );
            });
            builder.Services.AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
            {
                var configuration = services.GetRequiredService<IConfiguration>();
                var failedNotificationsQueue = configuration["serviceBusFailedNotificationsQueue"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(failedNotificationsQueue);
                return new ServiceBusQueueRepository(sender);
            });
            builder.Services.AddScoped<IThresholdNotificationConfig>((sp) =>
            {
                return new ThresholdNotificationConfig(true);
            });
            builder.Services.AddScoped<IThresholdNotificationService, ThresholdNotificationService>();
            builder.Services.AddHttpClient();
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
