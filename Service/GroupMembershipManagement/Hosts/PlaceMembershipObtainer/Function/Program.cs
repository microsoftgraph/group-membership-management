// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Repositories.Contracts;
using Repositories.BlobStorage;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Repositories.EntityFramework;
using Services.Contracts;
using BusinessLogic.SyncJobUpdater;
using Services;
using System;

namespace Hosts.PlaceMembershipObtainer
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
                    var functionName = "PlaceMembershipObtainer";
                    var dryRunSettingName = "PlaceMembershipObtainer:IsPlaceMembershipObtainerDryRunEnabled";
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.AddGraphAPIClient()
                        .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
                        .AddScoped<ISyncJobHistoryRepository, SyncJobHistoryRepository>()
                        .AddScoped<ISyncJobStatusService, SyncJobStatusService>()
                        .AddScoped<PlaceMembershipObtainerService>();

                    services.AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                    {
                        var config = s.GetService<IConfiguration>();
                        var storageAccountName = config["membershipStorageAccountName"];
                        var containerName = config["membershipContainerName"];

                        return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                    });

                    services.AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(s =>
                    {
                        var config = s.GetRequiredService<IConfiguration>();
                        var membershipAggregatorQueue = config["serviceBusMembershipAggregatorQueue"];
                        var client = s.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(membershipAggregatorQueue);
                        return new ServiceBusQueueRepository(sender);
                    });
                }).Build();

            host.Run();
        }
    }
}
