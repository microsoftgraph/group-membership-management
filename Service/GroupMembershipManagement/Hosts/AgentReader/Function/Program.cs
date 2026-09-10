// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.AgentsTable;
using Repositories.Contracts;
using Repositories.GraphAgents;
using Azure.Identity;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.AgentReader.Services;
using Hosts.AgentReader.Services.Contracts;
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
using System;

namespace Hosts.AgentReader
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
                    var endpoint = CommonServices.GetValueOrThrowBase(settings, "appConfigurationEndpoint");
                    config.AddAzureAppConfiguration(options =>
                    {
                        DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
                        options.Connect(new Uri(endpoint), credential).UseFeatureFlags();
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    CommonServices.ConfigureCommonServices(
                        services, configuration, "AgentReader", string.Empty, context.HostingEnvironment.ContentRootPath);
                    services.ConfigureFunctionsApplicationInsights();
                    services.AddSingleton(TimeProvider.System);

                    services.AddSingleton(provider =>
                    {
                        var credentials = provider.GetRequiredService<IOptions<GraphCredentials>>().Value;
                        var credential = FunctionAppDI.CreateAuthenticationProvider(credentials, credentials.AuthenticationType);
                        var handlers = GraphClientFactory.CreateDefaultHandlers();
                        // Nearest the transport so SDK retries/claims challenges cannot hide physical responses.
                        handlers.Add(new AgentGraphTelemetryHandler(
                            provider.GetRequiredService<ILogger<AgentGraphTelemetryHandler>>(),
                            provider.GetRequiredService<TelemetryClient>()));
                        return new GraphServiceClient(GraphClientFactory.Create(handlers), credential);
                    });
                    services.AddSingleton<IAgentSqlConnectionFactory>(_ => new AgentSqlConnectionFactory(
                        CommonServices.GetValueOrThrowBase(configuration, "sqlServerMSIConnectionString")));
                    services.AddScoped<IAgentGraphRepository, AgentGraphRepository>();
                    services.AddScoped<IAgentTableRepository, AgentTableRepository>();
                    services.AddScoped<IAgentReaderService, AgentReaderService>();
                })
                .Build();

            host.Run();
        }
    }
}
