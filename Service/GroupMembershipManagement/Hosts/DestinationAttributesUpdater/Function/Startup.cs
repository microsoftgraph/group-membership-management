// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.TeamsChannel;
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
            .AddGraphAPIClient()
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

            builder.Services.AddTransient<ITeamsChannelRepository, TeamsChannelRepository>((services) =>
            {
                var loggingRepository = services.GetRequiredService<ILoggingRepository>();
                var telemetryClient = services.GetRequiredService<TelemetryClient>();

                var configuration = services.GetService<IConfiguration>();
                var graphCredentials = services.GetService<IOptions<GraphCredentials>>().Value;
                graphCredentials.ServiceAccountUserName = configuration["teamsChannelServiceAccountUsername"];
                graphCredentials.ServiceAccountPassword = configuration["teamsChannelServiceAccountPassword"];
                var graphServiceClient = new GraphServiceClient(FunctionAppDI.CreateServiceAccountAuthProvider(graphCredentials));

                return new TeamsChannelRepository(loggingRepository, graphServiceClient, telemetryClient);
            });

            builder.Services.AddScoped<IDestinationAttributesUpdaterService, DestinationAttributesUpdaterService>();
        }
    }
}
