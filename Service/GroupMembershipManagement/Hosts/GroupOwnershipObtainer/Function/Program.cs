// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.EntityFramework;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services;
using Services.Contracts;
using BusinessLogic.SyncJobUpdater;
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
                    var functionName = "GroupOwnershipObtainer";
                    var dryRunSettingName = "GroupOwnershipObtainer:IsDryRunEnabled";
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
                    services.ConfigureFunctionsApplicationInsights();

                    services.AddGraphAPIClient();

                    services.AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                    {
                        var configuration = s.GetService<IConfiguration>();
                        var storageAccountName = configuration["membershipStorageAccountName"];
                        var containerName = configuration["membershipContainerName"];
                        return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                    });

                    services.AddScoped<IGraphGroupRepository, GraphGroupRepository>();
                    services.AddScoped<IGroupOwnershipObtainerService, GroupOwnershipObtainerService>();
                    services.AddScoped<ISyncJobStatusService, SyncJobStatusService>();

                    services.AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var membershipAggregatorQueue = configuration["serviceBusMembershipAggregatorQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(membershipAggregatorQueue);
                        return new ServiceBusQueueRepository(sender);
                    });
                }).Build();

            host.Run();
        }
    }
}
