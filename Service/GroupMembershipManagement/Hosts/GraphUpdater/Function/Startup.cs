// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using GraphUpdater.Entities;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services;
using Services.Contracts;
using System.Collections.Generic;
using System;
using System.Text.Json;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.GraphUpdater.Startup))]

namespace Hosts.GraphUpdater
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(GraphUpdater);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddOptions<DeltaCachingConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.DeltaCacheEnabled = GetBoolSetting(configuration, "GraphUpdater:IsDeltaCacheEnabled", false);
            });
            builder.Services.AddSingleton<IDeltaCachingConfig>(services =>
            {
                return new DeltaCachingConfig(services.GetService<IOptions<DeltaCachingConfig>>().Value.DeltaCacheEnabled);
            });

            builder.Services.AddGraphAPIClient()

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
                return new GraphUpdaterBatchSize { BatchSize = GetIntSetting(configuration, "GraphUpdater:UpdateBatchSize", 100) };
            })
            .AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
            {
                var configuration = services.GetRequiredService<IConfiguration>();
                var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(notificationsQueue);
                return new ServiceBusQueueRepository(sender);
            })
            .AddSingleton(services =>
            {
                var client = services.GetRequiredService<ServiceBusClient>();
                var serviceBusMembershipUpdatersTopic = GetValueOrThrow("serviceBusMembershipUpdatersTopic");
                var receiver = client.CreateReceiver(serviceBusMembershipUpdatersTopic, "GraphUpdater");
                return receiver;
            })
            .AddOptions<MultiLaneConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                configuration.GetSection("MultiLane").Bind(settings);
                settings.TriggerDelay = CommonServices.GetIntSettingBase(configuration, "triggerDelay", 0);
            })
            .Services.AddSingleton(services =>
            {
                var client = services.GetRequiredService<ServiceBusClient>();
                var serviceBusMembershipUpdatersTopic = CommonServices.GetValueOrThrowBase("serviceBusMembershipUpdatersTopic");
                var receiver = client.CreateReceiver(serviceBusMembershipUpdatersTopic, "GraphUpdater");
                return receiver;
            })
            .AddSingleton(services =>
            {
                var multilaneConfig = services.GetRequiredService<IOptions<MultiLaneConfig>>();
                var availableMembershipUpdaters = JsonSerializer.Deserialize<List<MembershipUpdater>>(multilaneConfig.Value.AvailableMembershipUpdaters);
                var currentLaneSize = CommonServices.GetValueOrDefaultBase("instanceIdentifier");
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
                membershipUpdaters.CurrentTopicName = CommonServices.GetValueOrThrowBase("serviceBusMembershipUpdatersTopic");
                return membershipUpdaters;
            });
        }

        private int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var isParsed = int.TryParse(configuration[settingName], out var maximumNumberOfThresholdRecipients);
            return isParsed ? maximumNumberOfThresholdRecipients : defaultValue;
        }

        private bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            if (checkParse)
                return value;
            return defaultValue;
        }
    }
}
