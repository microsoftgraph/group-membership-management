// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Services;
using Services.Contracts;

[assembly: FunctionsStartup(typeof(Hosts.DestinationAttributesUpdater.Startup))]

namespace Hosts.DestinationAttributesUpdater
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(DestinationAttributesUpdater);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton<IKeyVaultSecret<IDestinationAttributesUpdaterService>>(services => new KeyVaultSecret<IDestinationAttributesUpdaterService>(services.GetService<IOptions<GraphCredentials>>().Value.ClientId))
            .AddSingleton((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

            builder.Services.AddScoped<IDestinationAttributesUpdaterService, DestinationAttributesUpdaterService>();
        }
    }
}
