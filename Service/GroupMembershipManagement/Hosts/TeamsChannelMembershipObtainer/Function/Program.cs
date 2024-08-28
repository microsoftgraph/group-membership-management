// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Polly;
using Polly.Extensions.Http;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.ServiceBusQueue;
using Repositories.TeamsChannel;
using System;
using System.Net.Http;
using TeamsChannelMembershipObtainer.Service;
using TeamsChannelMembershipObtainer.Service.Contracts;
using Constants = TeamsChannelMembershipObtainer.Service.Constants;

namespace Hosts.TeamsChannelMembershipObtainer
{

    public class TeamsChannelMembershipObtainer
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
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    var functionName = "TeamsChannelMembershipObtainer";
                    var dryRunSettingName = string.Empty;
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
                    .AddTransient<ITeamsChannelService, TeamsChannelMembershipObtainerService>()
                    .AddTransient<ITeamsChannelRepository, TeamsChannelRepository>()
                    .AddScoped<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                    {
                        var configuration = services.GetService<IConfiguration>();
                        var membershipAggregatorQueue = configuration["serviceBusMembershipAggregatorQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(membershipAggregatorQueue);
                        return new ServiceBusQueueRepository(sender);
                    });

                    services.AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var failedNotificationsQueue = configuration["serviceBusFailedNotificationsQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(failedNotificationsQueue);
                        return new ServiceBusQueueRepository(sender);
                    });

                    services.AddHttpClient();

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