// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using BusinessLogic.SyncJobUpdater;
using Common.DependencyInjection;
using DIConcreteTypes;
using GraphUpdater.Entities;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Models;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.EntityFramework;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Hosts.GraphUpdater
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

                        options.Connect(new Uri(appConfigEndpoint), credential)
                            .UseFeatureFlags();
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;

                    var instanceIdentifier = CommonServices.GetValueOrDefaultBase(configuration, "instanceIdentifier");
                    var functionName = nameof(GraphUpdater);
                    var dryRunSettingName = string.Empty;
                    var rootPath = context.HostingEnvironment.ContentRootPath;

                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
                    services.ConfigureFunctionsApplicationInsights();

                    services.AddOptions<DeltaCachingConfig>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        settings.DeltaCacheEnabled = GetBoolSetting(configuration, "GraphUpdater:IsDeltaCacheEnabled", false);
                    });
                    services.AddSingleton<IDeltaCachingConfig>(services =>
                    {
                        return new DeltaCachingConfig(services.GetService<IOptions<DeltaCachingConfig>>().Value.DeltaCacheEnabled);
                    });

                    services.AddGraphAPIClient()
                    .AddSingleton<IGraphRepositorySettings>(services =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var addRequests = GetIntSetting(configuration, "concurrentAddRequests", 10);
                        var removeRequests = GetIntSetting(configuration, "concurrentRemoveRequests", 10);
                        return new GraphRepositorySettings
                        {
                            ConcurrentAddRequests = addRequests <= 0 || addRequests > 10 ? 10 : addRequests,
                            ConcurrentRemoveRequests = removeRequests <= 0 || removeRequests > 10 ? 10 : removeRequests
                        };
                    })
                    .AddScoped<ISyncJobHistoryRepository, SyncJobHistoryRepository>()
                    .AddScoped<ISyncJobStatusService, SyncJobStatusService>()
                    .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
                    .AddScoped<IGraphUpdaterService, GraphUpdaterService>()
                    .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                    {
                        var configuration = s.GetService<IConfiguration>();
                        var storageAccountName = configuration["membershipStorageAccountName"];
                        var containerName = configuration["membershipContainerName"];

                        return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                    })
                    .AddSingleton((s) =>
                    {
                        var configuration = s.GetService<IConfiguration>();
                        return new GraphUpdaterBatchSize { BatchSize = GetIntSetting(configuration, "GraphUpdater__UpdateBatchSize", 100) };
                    })
                    .AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(notificationsQueue);
                        return new ServiceBusQueueRepository(sender);
                    })
                    .AddKeyedSingleton<ServiceBusSender>("messageSplitterTopicSender", (services, _) =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var topicName = configuration["serviceBusMessageSplitterTopic"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        return client.CreateSender(topicName);
                    })
                    .AddSingleton(services =>
                    {
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var config = services.GetRequiredService<IConfiguration>();
                        var serviceBusMembershipUpdatersTopic = CommonServices.GetValueOrThrowBase(config, "serviceBusMembershipUpdatersTopic");
                        var receiver = client.CreateReceiver(serviceBusMembershipUpdatersTopic, "GraphUpdater");
                        return receiver;
                    })
                    .AddOptions<MultiLaneConfig>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        configuration.GetSection("MultiLane").Bind(settings);
                        settings.TriggerDelay = CommonServices.GetIntSettingBase(configuration, "triggerDelay", 0);
                    })
                    .Services.Configure<RunLimiterSettings>(settings =>
                    {
                        var instanceIdentifier = CommonServices.GetValueOrDefaultBase(configuration, "instanceIdentifier") ?? string.Empty;

                        if (instanceIdentifier.Equals("small", StringComparison.OrdinalIgnoreCase))
                        {
                            configuration.GetSection("MultiLane:Small:RateLimiter").Bind(settings);
                        }
                        else if (instanceIdentifier.Equals("large", StringComparison.OrdinalIgnoreCase))
                        {
                            configuration.GetSection("MultiLane:Large:RateLimiter").Bind(settings);
                        }
                        else
                        {
                            throw new Exception($"Unknown instance identifier: {instanceIdentifier}");
                        }
                    })
                    .AddSingleton(services => services.GetRequiredService<IOptions<RunLimiterSettings>>().Value)
                    .AddSingleton(services =>
                    {
                        var multilaneConfig = services.GetRequiredService<IOptions<MultiLaneConfig>>();
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var availableMembershipUpdaters = JsonSerializer.Deserialize<List<MembershipUpdater>>(multilaneConfig.Value.AvailableMembershipUpdaters);
                        var currentLaneSize = CommonServices.GetValueOrDefaultBase(configuration, "instanceIdentifier");
                        if (availableMembershipUpdaters == null) throw new Exception($"Unable to determine AvailableMembershipUpdaters");
                        var membershipUpdatersConfig = new MembershipUpdatersConfiguration
                        {
                            CurrentLaneSize = currentLaneSize,
                            MembershipUpdaters = availableMembershipUpdaters
                        };

                        var instances = new Dictionary<string, Dictionary<string, Subscription>>(StringComparer.InvariantCultureIgnoreCase);
                        foreach (var updater in membershipUpdatersConfig.MembershipUpdaters)
                        {
                            if (!instances.ContainsKey(updater.Name))
                            {
                                instances.Add(updater.Name, new Dictionary<string, Subscription>(StringComparer.InvariantCultureIgnoreCase));

                                foreach (var subscription in updater.Lanes)
                                {
                                    if (!instances[updater.Name].ContainsKey(subscription.Name))
                                    {
                                        instances[updater.Name].Add(subscription.Name, subscription);
                                    }
                                }
                            }
                        }

                        var membershipUpdaters = new MembershipUpdaters(currentLaneSize, instances);
                        membershipUpdaters.CurrentTopicName = CommonServices.GetValueOrThrowBase(configuration, "serviceBusMembershipUpdatersTopic");
                        return membershipUpdaters;
                    });
                }).Build();
            host.Run();
        }

        private static int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var isParsed = int.TryParse(configuration[settingName], out var value);
            return isParsed ? value : defaultValue;
        }

        private static bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            if (checkParse)
                return value;
            return defaultValue;
        }
    }
}
