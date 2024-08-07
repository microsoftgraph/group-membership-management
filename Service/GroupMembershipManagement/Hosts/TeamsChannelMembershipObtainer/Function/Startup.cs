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

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.TeamsChannelMembershipObtainer.Startup))]
namespace Hosts.TeamsChannelMembershipObtainer
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(TeamsChannelMembershipObtainer);

        protected override string DryRunSettingName => "TeamsChannelMembershipObtainer:IsDryRunEnabled";

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
        }

        // see https://learn.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/implement-http-call-retries-exponential-backoff-polly
        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode != System.Net.HttpStatusCode.NoContent)
                .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }
    }
}
