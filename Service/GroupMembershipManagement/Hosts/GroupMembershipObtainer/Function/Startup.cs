// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.GroupMembershipObtainer.Startup))]

namespace Hosts.GroupMembershipObtainer
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(GroupMembershipObtainer);
        protected override string DryRunSettingName => "GroupMembershipObtainer:IsDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddOptions<DeltaCachingConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.DeltaCacheEnabled = GetBoolSetting(configuration, "GroupMembershipObtainer:IsDeltaCacheEnabled", false);
            });
            builder.Services.AddSingleton<IDeltaCachingConfig>(services =>
            {
                return new DeltaCachingConfig(services.GetService<IOptions<DeltaCachingConfig>>().Value.DeltaCacheEnabled);
            });

            builder.Services.AddSingleton((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];

                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            })
            .AddScoped<SGMembershipCalculator>(services =>
            {
                var configuration = services.GetRequiredService<IConfiguration>();
                var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(notificationsQueue);
                var notificationsQueueRepository = new ServiceBusQueueRepository(sender);

                return new SGMembershipCalculator(
                    services.GetRequiredService<IGraphGroupRepository>(),
                    services.GetRequiredService<IBlobStorageRepository>(),
                    services.GetRequiredService<IDatabaseSyncJobsRepository>(),
                    notificationsQueueRepository,  
                    services.GetRequiredService<ILoggingRepository>(),
                    services.GetRequiredService<IDryRunValue>()
                );
            })
            .AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
            {
                var configuration = services.GetRequiredService<IConfiguration>();
                var membershipAggregatorQueue = configuration["serviceBusMembershipAggregatorQueue"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(membershipAggregatorQueue);
                return new ServiceBusQueueRepository(sender);
            });
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
