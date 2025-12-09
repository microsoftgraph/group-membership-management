// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using BusinessLogic.SyncJobUpdater;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.ServiceBusQueue;
using Repositories.EntityFramework;
using Repositories.TeamsChannel;
using Services.Contracts;
using Services.TeamsChannelUpdater;
using Services.TeamsChannelUpdater.Contracts;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.TeamsChannelUpdater.Startup))]
namespace Hosts.TeamsChannelUpdater
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(TeamsChannelUpdater);
        protected override string DryRunSettingName => "TeamsChannel:IsTeamsChannelDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton((services) =>
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
                var serviceBusMembershipUpdatersTopic = GetValueOrThrow("serviceBusMembershipUpdatersTopic");
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
            .AddScoped<ISyncJobHistoryRepository, SyncJobHistoryRepository>()
            .AddScoped<ISyncJobStatusService, SyncJobStatusService>()
            .AddTransient<ITeamsChannelUpdaterService, TeamsChannelUpdaterService>();
        }

        private bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            return checkParse ? value : defaultValue;
        }
    }
}
