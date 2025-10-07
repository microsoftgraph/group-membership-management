// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.ExternalConnectors;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Repositories.ServiceBusTopics;
using Services;
using Services.Contracts;

[assembly: FunctionsStartup(typeof(Hosts.MembershipAggregator.Startup))]

namespace Hosts.MembershipAggregator
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(MembershipAggregator);
        protected override string DryRunSettingName => "MembershipAggregator:IsMembershipAggregatorDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddOptions<MultiLaneConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                configuration.GetSection("MultiLane").Bind(settings);
            });

            builder.Services.AddOptions<ThresholdConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.MaximumNumberOfThresholdRecipients = GetIntSetting(configuration, "MaximumNumberOfThresholdRecipients", 10);
                settings.NumberOfThresholdViolationsToNotify = GetIntSetting(configuration, "NumberOfThresholdViolationsToNotify", 3);
                settings.NumberOfThresholdViolationsFollowUps = GetIntSetting(configuration, "NumberOfThresholdViolationsFollowUps", 3);
                settings.NumberOfThresholdViolationsToDisableJob = GetIntSetting(configuration, "NumberOfThresholdViolationsToDisableJob", 10);
            });

            builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MultiLaneConfig>>().Value);
            builder.Services.AddGraphAPIClient()
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddScoped<IGraphAPIService, GraphAPIService>((services) =>
            {
                var loggingRepository = services.GetRequiredService<ILoggingRepository>();
                var graphGroupRepository = services.GetRequiredService<IGraphGroupRepository>();

                var configuration = services.GetRequiredService<IConfiguration>();
                var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(notificationsQueue);
                var notificationsQueueRepository = new ServiceBusQueueRepository(sender);

                return new GraphAPIService(
                    loggingRepository,
                    graphGroupRepository,
                    notificationsQueueRepository
                );
            })
            .AddSingleton<IThresholdConfig>(services =>
            {
                return new ThresholdConfig
                    (
                        services.GetService<IOptions<ThresholdConfig>>().Value.MaximumNumberOfThresholdRecipients,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToNotify,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsFollowUps,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToDisableJob
                    );
            })
            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];

                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            })
            .AddSingleton<IServiceBusTopicsRepository>(services =>
            {
                var configuration = services.GetRequiredService<IConfiguration>();
                var membershipAggregatorQueue = configuration["serviceBusMembershipUpdatersTopic"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(membershipAggregatorQueue);
                return new ServiceBusTopicsRepository(sender);
            })
            .AddSingleton<ITopicMessageSenderService>((services) =>
            {
                var configuration = services.GetRequiredService<IConfiguration>();

                var loggingRepository = services.GetRequiredService<ILoggingRepository>();
                var membershipUpdatersSender = services.GetRequiredService<IServiceBusTopicsRepository>();

                var messageSplitterTopic = configuration["serviceBusMessageSplitterTopic"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(messageSplitterTopic);
                var messageSplitterSender = new ServiceBusTopicsRepository(sender);

                var multilaneConfig = services.GetRequiredService<IOptions<MultiLaneConfig>>()?.Value;

                return new TopicMessageSenderService(loggingRepository, membershipUpdatersSender, messageSplitterSender, multilaneConfig);
            })
            .AddScoped<IDeltaCalculatorService, DeltaCalculatorService>((services) =>
            {
                var syncJobRepository = services.GetRequiredService<IDatabaseSyncJobsRepository>();
                var groupsRepository = services.GetRequiredService<IDatabaseGroupsRepository>();
                var channelsRepository = services.GetRequiredService<IDatabaseChannelsRepository>();
                var loggingRepository = services.GetRequiredService<ILoggingRepository>();
                var graphAPIService = services.GetRequiredService<IGraphAPIService>();
                var dryRun = services.GetRequiredService<IDryRunValue>();
                var telemetryClient = services.GetRequiredService<TelemetryClient>();                
                var thresholdConfig = services.GetRequiredService<IThresholdConfig>();
                var thresholdNotificationConfig = services.GetRequiredService<IThresholdNotificationConfig>();
                var notificationRepository = services.GetRequiredService<INotificationRepository>();

                var configuration = services.GetRequiredService<IConfiguration>();
                var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(notificationsQueue);
                var notificationsQueueRepository = new ServiceBusQueueRepository(sender);

                return new DeltaCalculatorService(
                    syncJobRepository,
                    groupsRepository,
                    channelsRepository,
                    loggingRepository,
                    graphAPIService,
                    dryRun,
                    thresholdConfig,
                    thresholdNotificationConfig,
                    notificationRepository,
                    notificationsQueueRepository,
                    telemetryClient
                );
            });
        }

        private int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var isParsed = int.TryParse(configuration[settingName], out var maximumNumberOfThresholdRecipients);
            return isParsed ? maximumNumberOfThresholdRecipients : defaultValue;
        }
    }
}
