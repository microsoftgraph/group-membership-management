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
        var functionName = "TeamsChannelUpdater";
        var dryRunSettingName = "TeamsChannel:IsTeamsChannelDryRunEnabled";
        var rootPath = context.HostingEnvironment.ContentRootPath;
        CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

        services.AddSingleton((services) =>
        {
            var configuration = services.GetService<IConfiguration>();
            var graphCredentials = services.GetService<IOptions<GraphCredentials>>().Value;

            var channelReadWriteApplicationPermissionGranted = GetBoolSetting(configuration, "TeamsChannel:IsChannelReadWriteApplicationPermissionGranted", false);

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
        .AddTransient<ITeamsChannelRepository, TeamsChannelRepository>()
        .AddSingleton(services =>
        {
            var client = services.GetRequiredService<ServiceBusClient>();
            var serviceBusMembershipUpdatersTopic = CommonServices.GetValueOrThrowBase("serviceBusMembershipUpdatersTopic");
            var receiver = client.CreateReceiver(serviceBusMembershipUpdatersTopic, "TeamsChannelUpdater");
            return receiver;
        })
        .AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var notificationsQueue = configuration["serviceBusNotificationsQueue"];
            var client = services.GetRequiredService<ServiceBusClient>();
            var sender = client.CreateSender(notificationsQueue);
            return new ServiceBusQueueRepository(sender);
        })
        .AddTransient<ITeamsChannelUpdaterService, TeamsChannelUpdaterService>();
    })
    .Build();

host.Run();

static bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
{
    var checkParse = bool.TryParse(configuration[settingName], out bool value);
    return checkParse ? value : defaultValue;
}

