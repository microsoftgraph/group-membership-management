// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NonProdService.Activity.LoadTestingSyncJobCreator;
using NonProdService.LoadTestingPrepSubOrchestrator;
using Repositories.Contracts;
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
                    var appConfigEndpoint = CommonServices.GetValueOrThrowBase("appConfigurationEndpoint");

                    config.AddAzureAppConfiguration(options =>
                    {
                        options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential()).UseFeatureFlags();
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    var functionName = nameof(NonProdService);
                    var dryRunSettingName = string.Empty;
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.AddOptions<LoadTestingPrepSubOrchestratorOptions>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        settings.DestinationGroupOwnerId = configuration.GetValue<Guid>("graphCredentials:ClientId");
                        settings.GroupCount = configuration.GetValue<int>("NonProdService:LoadTesting:JobCount");
                    });

                    services.AddOptions<LoadTestingSyncJobCreatorOptions>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        configuration.GetSection("NonProdService:LoadTesting").Bind(settings);
                    });

                    services
                       .AddGraphAPIClient()
                       .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
                       .AddScoped<IGraphUserRepository, GraphUserRepository>()
                       .AddSingleton<INonProdService, Services.NonProdService>();

                }).Build();

            host.Run();
        }
    }
}
