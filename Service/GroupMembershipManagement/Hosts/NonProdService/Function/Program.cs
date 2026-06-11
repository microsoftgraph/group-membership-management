// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NonProdService.Activity.LoadTestingSyncJobCreator;
using NonProdService.LoadTestingPrepSubOrchestrator;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphAzureADUsers;
using Repositories.GraphGroups;
using Services.Contracts;
using System;

namespace Hosts.NonProdService
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
                    var functionName = "NonProdService";
                    var dryRunSettingName = string.Empty;
                    var rootPath = context.HostingEnvironment.ContentRootPath;

                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.AddGraphAPIClient();
                    services.AddScoped<IGraphGroupRepository, GraphGroupRepository>();
                    services.AddScoped<IGraphUserRepository, GraphUserRepository>();

                    services.AddOptions<LoadTestingPrepSubOrchestratorOptions>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        settings.DestinationGroupOwnerId = configuration.GetValue<Guid>("graphCredentials:ClientId");
                        settings.GroupCount = configuration.GetValue<int>("NonProdService:LoadTesting:JobCount");
                    });

                    services.AddOptions<LoadTestingSyncJobCreatorOptions>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        configuration.GetSection("NonProdService:LoadTesting").Bind(settings);
                    });

                    services.AddSingleton<INonProdService, Services.NonProdService>();
                })
                .Build();

            host.Run();
        }
    }
}
