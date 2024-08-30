// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.


using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using GraphUpdater.Entities;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services;
using Services.Contracts;
using System;

namespace Hosts.GraphUpdater
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
                var functionName = "GraphUpdater";
                var dryRunSettingName = string.Empty;
                var rootPath = context.HostingEnvironment.ContentRootPath;
                CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
                services.AddOptions<DeltaCachingConfig>().Configure<IConfiguration>((settings, configuration) =>
                {
                    settings.DeltaCacheEnabled = CommonServices.GetBoolSettingBase(configuration, "GraphUpdater:IsDeltaCacheEnabled", false);
                });
                services.AddSingleton<IDeltaCachingConfig>(services =>
                {
                    return new DeltaCachingConfig(services.GetService<IOptions<DeltaCachingConfig>>().Value.DeltaCacheEnabled);
                });

                services.AddGraphAPIClient()

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
                    return new GraphUpdaterBatchSize { BatchSize = CommonServices.GetIntSettingBase(configuration, "GraphUpdater:UpdateBatchSize", 100) };
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
                    var serviceBusMembershipUpdatersTopic = CommonServices.GetValueOrThrowBase("serviceBusMembershipUpdatersTopic");
                    var receiver = client.CreateReceiver(serviceBusMembershipUpdatersTopic, "GraphUpdater");
                    return receiver;
                });
                })
                .Build();
                host.Run();
        }
    }
}