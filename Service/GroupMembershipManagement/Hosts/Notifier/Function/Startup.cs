// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Hosts.Notifier;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Services;
using Services.Contracts;

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.Notifier
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(Notifier);
        protected override string DryRunSettingName => string.Empty;
        protected string NotifierConfigSettingName => "Notifier:NotifierConfiguration";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddScoped<INotifierService, NotifierService>();

            builder.Services.AddHttpClient();
        }
    }
}
