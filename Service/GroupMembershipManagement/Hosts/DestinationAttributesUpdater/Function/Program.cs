// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.


using Azure.Identity;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.TeamsChannel;
using Services;
using Services.Contracts;
using System;

namespace Hosts.DestinationAttributesUpdater
{

    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureAppConfiguration((context, config) =>
                {
                    var settings = config.Build();
                    var appConfigEndpoint = CommonServices.GetValueOrThrowBase("appConfigurationEndpoint");

                    config.AddAzureAppConfiguration(options =>
                    {
                        options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential())
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
                services
                    .AddGraphAPIClient()
                    .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

                services.AddTransient<ITeamsChannelRepository, TeamsChannelRepository>((services) =>
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

                services.AddScoped<IDestinationAttributesUpdaterService, DestinationAttributesUpdaterService>();
                })
                .Build();

                host.Run();
        }
    }
}