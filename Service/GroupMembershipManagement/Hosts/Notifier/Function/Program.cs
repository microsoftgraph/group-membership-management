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

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        var functionName = "Notifier";
        var dryRunSettingName = string.Empty;
        var rootPath = context.HostingEnvironment.ContentRootPath;
        CommonServices.ConfigureCommonServices(services, configuration, "Notifier", dryRunSettingName, rootPath);
        services.AddOptions<HandleInactiveJobsConfig>().Configure<IConfiguration>((settings, configuration) =>
        {
            settings.HandleInactiveJobsEnabled = GetBoolSetting(configuration, "AzureMaintenance:HandleInactiveJobsEnabled", false);
            settings.NumberOfDaysBeforeDeletion = GetIntSetting(configuration, "AzureMaintenance:NumberOfDaysBeforeDeletion", 0);
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
            settings.MaximumNumberOfThresholdRecipients = GetIntSetting(configuration, "MaximumNumberOfThresholdRecipients", 10);
            settings.NumberOfThresholdViolationsToNotify = GetIntSetting(configuration, "NumberOfThresholdViolationsToNotify", 3);
            settings.NumberOfThresholdViolationsFollowUps = GetIntSetting(configuration, "NumberOfThresholdViolationsFollowUps", 3);
            settings.NumberOfThresholdViolationsToDisableJob = GetIntSetting(configuration, "NumberOfThresholdViolationsToDisableJob", 10);
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
static bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
{
    var checkParse = bool.TryParse(configuration[settingName], out bool value);
    if (checkParse)
        return value;
    return defaultValue;
}

static int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
{
    var checkParse = int.TryParse(configuration[settingName], out int value);
    if (checkParse)
        return value;
    return defaultValue;
}