// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Hosts.FunctionBase;
using Services;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Common.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.GraphGroups;
using Microsoft.Graph;

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
           .AddSingleton<IGraphServiceClient>((services) =>
           {
               return new GraphServiceClient(FunctionAppDI.CreateAuthProviderFromSecret(services.GetService<IOptions<GraphCredentials>>().Value));
           })
            .AddSingleton<IGraphGroupRepository, GraphGroupRepository>();

            builder.Services.AddSingleton<INonProdService, Services.NonProdService>();
        }
    }
}
