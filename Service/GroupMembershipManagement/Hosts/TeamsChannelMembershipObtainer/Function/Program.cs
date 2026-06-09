// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.ServiceBusQueue;
using Repositories.TeamsChannel;
using System;
using BusinessLogic.SyncJobUpdater;
using Services.Contracts;
using TeamsChannelMembershipObtainer.Service;
using TeamsChannelMembershipObtainer.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
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
                    var functionName = "TeamsChannelMembershipObtainer";
                    var dryRunSettingName = "TeamsChannelMembershipObtainer:IsDryRunEnabled";
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.ConfigureFunctionsApplicationInsights();

                    services.AddScoped<ISyncJobStatusService, SyncJobStatusService>();

                    services.AddSingleton((services) =>
                    {
                        var configuration = services.GetService<IConfiguration>();
                        var graphCredentials = services.GetService<IOptions<GraphCredentials>>().Value;

                        var channelReadWriteApplicationPermissionGranted = CommonServices.GetBoolSettingBase(configuration, "TeamsChannel:IsChannelReadWriteApplicationPermissionGranted", false);

                        TokenCredential graphTokenCredential;
                        if (channelReadWriteApplicationPermissionGranted)
                        {
                            graphTokenCredential = FunctionAppDI.CreateAuthProviderFromSecret(graphCredentials);
                        }
                        else
                        {
                            graphCredentials.ServiceAccountUserName = configuration["teamsChannelServiceAccountUsername"];
                            graphCredentials.ServiceAccountPassword = configuration["teamsChannelServiceAccountPassword"];

                            graphTokenCredential = FunctionAppDI.CreateServiceAccountAuthProvider(graphCredentials);
                        }
                        return new GraphServiceClient(graphTokenCredential);
                    })
                    .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                    {
                        var configuration = s.GetService<IConfiguration>();
                        var storageAccountName = configuration["membershipStorageAccountName"];
                        var containerName = configuration["membershipContainerName"];

                        return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                    })
                    .AddScoped<ITeamsChannelService, TeamsChannelMembershipObtainerService>()
                    .AddTransient<ITeamsChannelRepository, TeamsChannelRepository>()
                    .AddScoped<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                    {
                        var configuration = services.GetService<IConfiguration>();
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
