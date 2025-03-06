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
