// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services;
using Services.Contracts;

[assembly: FunctionsStartup(typeof(Hosts.GroupOwnershipObtainer.Startup))]
namespace Hosts.GroupOwnershipObtainer
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(GroupOwnershipObtainer);

        protected override string DryRunSettingName => "GroupOwnershipObtainer:IsDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddGraphAPIClient()
            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];
                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddScoped<IGroupOwnershipObtainerService, GroupOwnershipObtainerService>()
            .AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
            {
                var configuration = services.GetRequiredService<IConfiguration>();
                var membershipAggregatorQueue = configuration["serviceBusMembershipAggregatorQueue"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(membershipAggregatorQueue);
                return new ServiceBusQueueRepository(sender);
            });
        }
    }
}
