// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NonProdService.Activity.LoadTestingSyncJobCreator;
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

            builder.Services
            .AddGraphAPIClient()
            .AddSingleton<IGraphGroupRepository, GraphGroupRepository>()
            .AddSingleton<IGraphUserRepository, GraphUserRepository>();

            builder.Services.AddOptions<LoadTestingPrepSubOrchestratorOptions>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.DestinationGroupOwnerId = configuration.GetValue<Guid>("graphCredentials:ClientId");
                settings.GroupCount = configuration.GetValue<int>("NonProdService:LoadTesting:JobCount");
            });

            builder.Services.AddOptions<LoadTestingSyncJobCreatorOptions>().Configure<IConfiguration>((settings, configuration) =>
            {
                configuration.GetSection("NonProdService:LoadTesting").Bind(settings);
            });

            builder.Services.AddSingleton<INonProdService, Services.NonProdService>();
        }
    }
}
