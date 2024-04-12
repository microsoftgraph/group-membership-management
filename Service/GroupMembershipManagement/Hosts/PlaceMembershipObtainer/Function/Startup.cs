// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Services;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.PlaceMembershipObtainer.Startup))]

namespace Hosts.PlaceMembershipObtainer
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(PlaceMembershipObtainer);
        protected override string DryRunSettingName => "PlaceMembershipObtainer:IsPlaceMembershipObtainerDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddGraphAPIClient()
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddScoped<PlaceMembershipObtainerService>()
            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];

                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
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
    }
}
