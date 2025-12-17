// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using BusinessLogic.SyncJobUpdater;
using DIConcreteTypes;
using Hosts.FunctionBase;
using MessageSplitter.Contracts;
using MessageSplitter.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Models;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.ServiceBusTopics;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Services.Contracts;

namespace Hosts.MessageSplitter
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
                            var instanceIdentifier = CommonServices.GetValueOrThrowBase(configuration, "instanceIdentifier");
                            var functionName = $"MessageSplitter_{instanceIdentifier}";
                            var dryRunSettingName = string.Empty;
                            var rootPath = context.HostingEnvironment.ContentRootPath;
                            CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                            services.Configure<MultiLaneConfig>(configuration.GetSection("MultiLane"));
                            services.Configure<RunLimiterSettings>(settings =>
                            {
                                if (instanceIdentifier.Equals("s1", StringComparison.OrdinalIgnoreCase))
                                {
                                    configuration.GetSection("MultiLane:Small:RateLimiter").Bind(settings);
                                }
                                else if (instanceIdentifier.Equals("l1", StringComparison.OrdinalIgnoreCase))
                                {
                                    configuration.GetSection("MultiLane:Large:RateLimiter").Bind(settings);
                                }
                                else
                                {
                                    throw new Exception($"Unknown instance identifier: {instanceIdentifier}");
                                }
                            });

                            services.AddSingleton<RunLimiterSettings>(services => services.GetRequiredService<IOptions<RunLimiterSettings>>().Value);

                            services.AddKeyedSingleton<IServiceBusTopicsRepository>("membershipUpdaterSender", (services, _) =>
                            {
                                var messageSplitterTopic = configuration["serviceBusMembershipUpdatersTopic"];
                                var client = services.GetRequiredService<ServiceBusClient>();
                                var sender = client.CreateSender(messageSplitterTopic);
                                return new ServiceBusTopicsRepository(sender);
                            })
                            .AddKeyedSingleton<ServiceBusSender>("messageSplitterTopicSender", (services, _) =>
                            {
                                var topicName = configuration["serviceBusMessageSplitterTopic"];
                                var client = services.GetRequiredService<ServiceBusClient>();
                                return client.CreateSender(topicName);
                            })
                            .AddKeyedSingleton<IServiceBusTopicsRepository>("messageSplitterTopicSenderRepository", (services, _) =>
                            {
                                var topicName = configuration["serviceBusMessageSplitterTopic"];
                                var client = services.GetRequiredService<ServiceBusClient>();
                                var sender = client.CreateSender(topicName);
                                return new ServiceBusTopicsRepository(sender);
                            })
                            .AddKeyedSingleton<ServiceBusReceiver>("messageSplitterPendingReceiver", (services, _) =>
                            {
                                var configuration = services.GetRequiredService<IConfiguration>();
                                var topicName = configuration["serviceBusMessageSplitterTopic"];
                                var pendingSubscriptionName = CommonServices.GetValueOrThrowBase(configuration, "messageSplitterPendingSubscription");
                                var client = services.GetRequiredService<ServiceBusClient>();
                                return client.CreateReceiver(topicName, pendingSubscriptionName, new ServiceBusReceiverOptions
                                {
                                    ReceiveMode = ServiceBusReceiveMode.PeekLock
                                });
                            })
                            .AddSingleton(services =>
                            {
                                var multilaneConfig = services.GetRequiredService<IOptions<MultiLaneConfig>>();
                                var configuration = services.GetRequiredService<IConfiguration>();
                                var availableMembershipUpdaters = JsonSerializer.Deserialize<List<MembershipUpdater>>(multilaneConfig.Value.AvailableMembershipUpdaters);
                                var currentLaneSize = CommonServices.GetValueOrThrowBase(configuration, "messageSplitterSubscription");
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

                                return new MembershipUpdaters(currentLaneSize, instances);
                            })
                            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                            {
                                var storageAccountName = configuration["membershipStorageAccountName"];
                                var containerName = configuration["membershipContainerName"];
                                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                            })
                            .AddScoped<IMessageSplitterService, MessageSplitterService>()
                            .AddScoped<ISyncJobStatusService, SyncJobStatusService>();



                        }).Build();

            host.Run();
        }
    }
}