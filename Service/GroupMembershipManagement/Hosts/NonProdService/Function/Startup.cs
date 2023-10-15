// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using NonProdService.LoadTestingPrepSubOrchestrator;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphAzureADUsers;
using Repositories.GraphGroups;
using Services.Contracts;
using System;

[assembly: FunctionsStartup(typeof(Hosts.NonProdService.Startup))]

namespace Hosts.NonProdService
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(NonProdService);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton<IKeyVaultSecret<INonProdService>>(services => new KeyVaultSecret<INonProdService>(services.GetService<IOptions<GraphCredentials>>().Value.ClientId))
           .AddSingleton((services) =>
           {
               return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
           })
            .AddSingleton<IGraphGroupRepository, GraphGroupRepository>()
            .AddSingleton<IGraphUserRepository, GraphUserRepository>()
            .AddOptions<LoadTestingPrepSubOrchestratorOptions>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.DestinationGroupOwnerId = configuration.GetValue<Guid>("graphCredentials:ClientId");
            });

            builder.Services.AddSingleton<INonProdService, Services.NonProdService>();
        }
    }
}
