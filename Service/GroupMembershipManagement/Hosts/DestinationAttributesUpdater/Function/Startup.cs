// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
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

            builder.Services
            .AddGraphAPIClient()
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

            builder.Services.Configure<GraphCredentials>("TeamsGraphCredentials", builder.GetContext().Configuration.GetSection("TeamsGraphCredentials"));

            builder.Services.AddTransient<ITeamsChannelRepository, TeamsChannelRepository>((services) =>
            {
                var loggingRepository = services.GetRequiredService<ILoggingRepository>();
                var telemetryClient = services.GetRequiredService<TelemetryClient>();

                var configuration = services.GetService<IConfiguration>();
                var teamsGraphCredentials = services.GetService<IOptionsSnapshot<GraphCredentials>>().Get("TeamsGraphCredentials");
                var channelReadWriteApplicationPermissionGranted = GetBoolSetting(configuration, "TeamsChannel:IsChannelReadWriteApplicationPermissionGranted", false);

                TokenCredential graphTokenCredential;

                if (channelReadWriteApplicationPermissionGranted)
                {
                    graphTokenCredential = FunctionAppDI.CreateAuthProviderFromSecret(teamsGraphCredentials);
                }
                else
                {
                    teamsGraphCredentials.ServiceAccountUserName = configuration["teamsChannelServiceAccountUsername"];
                    teamsGraphCredentials.ServiceAccountPassword = configuration["teamsChannelServiceAccountPassword"];

                    graphTokenCredential = FunctionAppDI.CreateServiceAccountAuthProvider(teamsGraphCredentials);
                }
                var graphServiceClient = new GraphServiceClient(graphTokenCredential);

                return new TeamsChannelRepository(loggingRepository, graphServiceClient, telemetryClient);
            });

            builder.Services.AddScoped<IDestinationAttributesUpdaterService, DestinationAttributesUpdaterService>();
        }

        private bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            return checkParse ? value : defaultValue;
        }
    }
}
