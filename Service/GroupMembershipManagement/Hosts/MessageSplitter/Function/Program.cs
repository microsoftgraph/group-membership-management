// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using Hosts.FunctionBase;
using MessageSplitter.Contracts;
using MessageSplitter.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.ServiceBusTopics;
using System.Text.Json;

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
                                TokenCredential credential;
#if DEBUG
                                credential = new DefaultAzureCredential();
#else
                                credential = new ManagedIdentityCredential();
#endif

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

                            services.AddKeyedSingleton<IServiceBusTopicsRepository>("membershipUpdaterSender", (services, _) =>
                            {
                                var messageSplitterTopic = configuration["serviceBusMembershipUpdatersTopic"];
                                var client = services.GetRequiredService<ServiceBusClient>();
                                var sender = client.CreateSender(messageSplitterTopic);
                                return new ServiceBusTopicsRepository(sender);
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
                            .AddScoped<IMessageSplitterService, MessageSplitterService>();



                        }).Build();

            host.Run();
        }
    }
}