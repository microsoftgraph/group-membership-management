using BusinessLogic.SyncJobUpdater;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Repositories.ServiceBusTopics;
using Repositories.EntityFramework;
using Services;
using Services.Contracts;
using System;

namespace Hosts.MembershipAggregator
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
                    var functionName = "MembershipAggregator";
                    var dryRunSettingName = "MembershipAggregator:IsMembershipAggregatorDryRunEnabled";
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
                    services.ConfigureFunctionsApplicationInsights();

                    services.AddOptions<MultiLaneConfig>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        configuration.GetSection("MultiLane").Bind(settings);
                    });

                    services.AddOptions<ThresholdConfig>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        settings.MaximumNumberOfThresholdRecipients = CommonServices.GetIntSettingBase(configuration, "MaximumNumberOfThresholdRecipients", 10);
                        settings.NumberOfThresholdViolationsToNotify = CommonServices.GetIntSettingBase(configuration, "NumberOfThresholdViolationsToNotify", 3);
                        settings.NumberOfThresholdViolationsFollowUps = CommonServices.GetIntSettingBase(configuration, "NumberOfThresholdViolationsFollowUps", 3);
                        settings.NumberOfThresholdViolationsToDisableJob = CommonServices.GetIntSettingBase(configuration, "NumberOfThresholdViolationsToDisableJob", 10);
                    });

                    services
                    .AddSingleton(sp => sp.GetRequiredService<IOptions<MultiLaneConfig>>().Value)
                    .AddGraphAPIClient()
                    .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
                    .AddScoped<IGraphAPIService, GraphAPIService>((services) =>
                    {
                        var logger = services.GetRequiredService<ILogger<GraphAPIService>>();
                        var graphGroupRepository = services.GetRequiredService<IGraphGroupRepository>();

                        var configuration = services.GetRequiredService<IConfiguration>();
                        var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(notificationsQueue);
                        var notificationsQueueRepository = new ServiceBusQueueRepository(sender);

                        return new GraphAPIService(
                            logger,
                            graphGroupRepository,
                            notificationsQueueRepository
                        );
                    })
                    .AddScoped<ISyncJobHistoryRepository, SyncJobHistoryRepository>()
                    .AddScoped<ISyncJobStatusService, SyncJobStatusService>()
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

                        var logger = services.GetRequiredService<ILogger<TopicMessageSenderService>>();
                        var membershipUpdatersSender = services.GetRequiredService<IServiceBusTopicsRepository>();

                        var messageSplitterTopic = configuration["serviceBusMessageSplitterTopic"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(messageSplitterTopic);
                        var messageSplitterSender = new ServiceBusTopicsRepository(sender);

                        var multilaneConfig = services.GetRequiredService<IOptions<MultiLaneConfig>>()?.Value;

                        return new TopicMessageSenderService(logger, membershipUpdatersSender, messageSplitterSender, multilaneConfig);
                    })
                    .AddScoped<IDeltaCalculatorService, DeltaCalculatorService>((services) =>
                    {
                        var syncJobRepository = services.GetRequiredService<IDatabaseSyncJobsRepository>();
                        var groupsRepository = services.GetRequiredService<IDatabaseGroupsRepository>();
                        var channelsRepository = services.GetRequiredService<IDatabaseChannelsRepository>();
                        var logger = services.GetRequiredService<ILogger<DeltaCalculatorService>>();
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
                            logger,
                            graphAPIService,
                            dryRun,
                            thresholdConfig,
                            thresholdNotificationConfig,
                            notificationRepository,
                            notificationsQueueRepository,
                            telemetryClient
                        );
                    });


                }).Build();

            host.Run();
        }
    }
}
