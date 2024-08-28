// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services;
using System;

namespace Hosts.GroupOwnershipObtainer
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
                    var appConfigEndpoint = CommonServices.GetValueOrThrowBase("appConfigurationEndpoint");

                    config.AddAzureAppConfiguration(options =>
                    {
                        options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential())
                            .UseFeatureFlags();
                    });
                })
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    var functionName = "GroupOwnershipObtainer";
                    var dryRunSettingName = string.Empty;
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.AddGraphAPIClient()
                        .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
                        .AddScoped<GroupOwnershipObtainerService>()
                        .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                        {
                            var configuration = s.GetService<IConfiguration>();
                            var storageAccountName = configuration["membershipStorageAccountName"];
                            var containerName = configuration["membershipContainerName"];
                            return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                        })
                        .AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                        {
                            var configuration = services.GetRequiredService<IConfiguration>();
                            var membershipAggregatorQueue = configuration["serviceBusMembershipAggregatorQueue"];
                            var client = services.GetRequiredService<ServiceBusClient>();
                            var sender = client.CreateSender(membershipAggregatorQueue);
                            return new ServiceBusQueueRepository(sender);
                        });
                })
                .Build();
            host.Run();
        }
    }
}