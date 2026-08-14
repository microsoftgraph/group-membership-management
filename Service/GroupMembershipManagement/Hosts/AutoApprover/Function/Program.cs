// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.DataFactory;
using Repositories.GraphGroups;
using Repositories.SqlMembershipRepository;
using Services.AutoApprover;
using Services.AutoApprover.Contracts;
using System;

namespace Hosts.AutoApprover
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
                        options.Connect(new Uri(appConfigEndpoint), credential).UseFeatureFlags();
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    var functionName = "AutoApprover";
                    var dryRunSettingName = string.Empty;
                    var rootPath = context.HostingEnvironment.ContentRootPath;

                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.ConfigureFunctionsApplicationInsights();

                    services.AddGraphAPIClient();
                    services.AddScoped<IGraphGroupRepository, GraphGroupRepository>();

                    services.AddSingleton<IKeyVaultSecret<ISqlMembershipRepository>>(_ =>
                        new KeyVaultSecret<ISqlMembershipRepository>(CommonServices.GetValueOrThrowBase(configuration, "sqlServerMSIConnectionString")));
                    services.AddSingleton<ISqlMembershipRepository, SqlMembershipRepository>();

                    services.AddSingleton<IDataFactorySecret<IDataFactoryRepository>>(new DataFactorySecrets<IDataFactoryRepository>(
                        CommonServices.GetValueOrThrowBase(configuration, "pipeline"),
                        CommonServices.GetValueOrThrowBase(configuration, "dataFactoryName"),
                        CommonServices.GetValueOrThrowBase(configuration, "subscriptionId"),
                        CommonServices.GetValueOrThrowBase(configuration, "dataResourceGroup")));
                    services.AddSingleton<IDataFactoryRepository, DataFactoryRepository>();

                    services.AddScoped<IAutoApproverService, AutoApproverService>();
                })
                .Build();

            host.Run();
        }
    }
}
