// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.DataFactory;
using Services;
using System;

namespace SqlDataChecker
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
                    var functionName = "SqlDataChecker";
                    var dryRunSettingName = "SqlDataChecker:IsSqlDataCheckerDryRunEnabled";
                    var rootPath = context.HostingEnvironment.ContentRootPath;

                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
                    services.ConfigureFunctionsApplicationInsights();

                    services.AddSingleton<IKeyVaultSecret<SqlDataCheckerValidatorService>>(services =>
                        new KeyVaultSecret<SqlDataCheckerValidatorService>(CommonServices.GetValueOrThrowBase(configuration, "sqlServerBasicConnectionString")));

                    services.AddSingleton<IDataFactorySecret<IDataFactoryRepository>>(new DataFactorySecrets<IDataFactoryRepository>(
                        CommonServices.GetValueOrThrowBase(configuration, "pipeline"),
                        CommonServices.GetValueOrThrowBase(configuration, "dataFactoryName"),
                        CommonServices.GetValueOrThrowBase(configuration, "subscriptionId"),
                        CommonServices.GetValueOrThrowBase(configuration, "dataResourceGroup")));

                    services.AddSingleton<IDataFactoryRepository, DataFactoryRepository>();
                    services.AddScoped<SqlDataCheckerValidatorService>();
                })
                .Build();

            host.Run();
        }
    }
}
