// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Azure.Identity;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.TeamsChannel;
using Services;
using Services.Contracts;
using System;

var host = new HostBuilder()
.ConfigureFunctionsWorkerDefaults()
.ConfigureAppConfiguration((context, config) =>
{
    var settings = config.Build();
    var appConfigEndpoint = CommonServices.GetValueOrThrowBase(settings, "appConfigurationEndpoint");

    config.AddAzureAppConfiguration(options =>
    {
        DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
        options.Connect(new Uri(appConfigEndpoint), credential)
            .UseFeatureFlags();
    });
})
.ConfigureServices((context, services) =>
{
    var configuration = context.Configuration;
    var functionName = "DestinationAttributesUpdater";
    var dryRunSettingName = string.Empty;
    var rootPath = context.HostingEnvironment.ContentRootPath;
    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
    services.ConfigureFunctionsApplicationInsights();

    services.AddGraphAPIClient();
    services.AddScoped<IGraphGroupRepository, GraphGroupRepository>();

    services.Configure<GraphCredentials>("TeamsGraphCredentials", configuration.GetSection("TeamsGraphCredentials"));

    services.AddTransient<ITeamsChannelRepository, TeamsChannelRepository>((services) =>
    {
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

        var teamsChannelRepositoryLogger = services.GetRequiredService<ILogger<TeamsChannelRepository>>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        return new TeamsChannelRepository(graphServiceClient, telemetryClient, teamsChannelRepositoryLogger, loggerFactory);
    });

    services.AddScoped<IDestinationAttributesUpdaterService, DestinationAttributesUpdaterService>();
}).Build();

host.Run();

static bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
{
    var checkParse = bool.TryParse(configuration[settingName], out bool value);
    return checkParse ? value : defaultValue;
}
