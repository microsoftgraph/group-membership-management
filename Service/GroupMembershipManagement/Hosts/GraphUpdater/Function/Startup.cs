// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Entities;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.MembershipDifference;
using Services;
using Services.Contracts;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.GraphUpdater.Startup))]

namespace Hosts.GraphUpdater
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(GraphUpdater);
        protected override string DryRunSettingName => "GraphUpdater:IsGraphUpdaterDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton<IMembershipDifferenceCalculator<AzureADUser>, MembershipDifferenceCalculator<AzureADUser>>()
            .AddSingleton<IGraphServiceClient>((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthProviderFromSecret(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddSingleton<IGraphGroupRepository, GraphGroupRepository>()
            .AddSingleton<MessageCollector>()
            .AddSingleton<IDeltaCalculatorService, DeltaCalculatorService>()
            .AddSingleton<IGraphUpdaterService, GraphUpdaterService>()
            .AddSingleton<IServiceBusMessageService, ServiceBusMessageService>();
        }
    }
}
