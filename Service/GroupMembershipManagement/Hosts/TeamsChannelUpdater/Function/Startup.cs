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
using Repositories.TeamsChannel;
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
                graphCredentials.ServiceAccountUserName = configuration["teamsChannelServiceAccountUsername"];
                graphCredentials.ServiceAccountPassword = configuration["teamsChannelServiceAccountPassword"];
                return new GraphServiceClient(FunctionAppDI.CreateServiceAccountAuthProvider(graphCredentials));
            })
            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];

                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            })
            .AddTransient<ITeamsChannelUpdaterService, TeamsChannelUpdaterService>()
            .AddTransient<ITeamsChannelRepository, TeamsChannelRepository>()
            .AddSingleton(services =>
            {
                var client = services.GetRequiredService<ServiceBusClient>();
                var serviceBusMembershipUpdatersTopic = GetValueOrThrow("serviceBusMembershipUpdatersTopic");
                var receiver = client.CreateReceiver(serviceBusMembershipUpdatersTopic, "TeamsChannelUpdater");
                return receiver;
            });
        }
    }
}
