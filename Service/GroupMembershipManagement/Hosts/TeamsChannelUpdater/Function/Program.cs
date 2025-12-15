// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.ServiceBusQueue;
using Repositories.TeamsChannel;
using Services.TeamsChannelUpdater;
using Services.TeamsChannelUpdater.Contracts;
using System;
using Repositories.EntityFramework;
using Services.Contracts;
using BusinessLogic.SyncJobUpdater;

namespace Hosts.TeamsChannelUpdater
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
                    var functionName = "TeamsChannelUpdater";
                    var dryRunSettingName = "TeamsChannel__IsTeamsChannelDryRunEnabled";
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.AddSingleton((serviceProvider) =>
                    {
                        var config = serviceProvider.GetService<IConfiguration>();
                        var graphCredentials = serviceProvider.GetService<IOptions<GraphCredentials>>().Value;

                        var channelReadWriteApplicationPermissionGranted = GetBoolSetting(config, "TeamsChannel__IsChannelReadWriteApplicationPermissionGranted", false);

                        TokenCredential graphTokenCredential;
                        if (channelReadWriteApplicationPermissionGranted)
                        {
                            graphTokenCredential = FunctionAppDI.CreateAuthProviderFromSecret(graphCredentials);
                        }
                        else
                        {
                            graphCredentials.ServiceAccountUserName = config["teamsChannelServiceAccountUsername"];
                            graphCredentials.ServiceAccountPassword = config["teamsChannelServiceAccountPassword"];

                            graphTokenCredential = FunctionAppDI.CreateServiceAccountAuthProvider(graphCredentials);
                        }
                        return new GraphServiceClient(graphTokenCredential);
                    })
                    .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                    {
                        var config = s.GetService<IConfiguration>();
                        var storageAccountName = CommonServices.GetValueOrThrowBase(config, "membershipStorageAccountName");
                        var containerName = CommonServices.GetValueOrThrowBase(config, "membershipContainerName");

                        return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                    })
                    .AddTransient<ITeamsChannelRepository, TeamsChannelRepository>()
                    .AddSingleton<ServiceBusReceiver>(serviceProvider =>
                    {
                        var client = serviceProvider.GetRequiredService<ServiceBusClient>();
                        var config = serviceProvider.GetRequiredService<IConfiguration>();
                        var serviceBusMembershipUpdatersTopic = config["serviceBusMembershipUpdatersTopic"];
                        var receiver = client.CreateReceiver(serviceBusMembershipUpdatersTopic, "TeamsChannelUpdater");
                        return receiver;
                    })
                    .AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(serviceProvider =>
                    {
                        var config = serviceProvider.GetRequiredService<IConfiguration>();
                        var notificationsQueue = config["serviceBusNotificationsQueue"];
                        var client = serviceProvider.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(notificationsQueue);
                        return new ServiceBusQueueRepository(sender);
                    })
                    .AddTransient<ITeamsChannelUpdaterService, TeamsChannelUpdaterService>()
                    .AddScoped<ISyncJobHistoryRepository, SyncJobHistoryRepository>()
                    .AddScoped<ISyncJobStatusService, SyncJobStatusService>();
                })
                .Build();

            host.Run();
        }

        private static bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            return checkParse ? value : defaultValue;
        }
    }
}
