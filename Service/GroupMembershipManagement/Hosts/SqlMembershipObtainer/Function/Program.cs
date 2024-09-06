// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.DataFactory;
using Repositories.ServiceBusQueue;
using Repositories.SqlMembershipRepository;
using Services;
using Services.Contracts;
using System;

namespace SqlMembershipObtainer
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
                    var functionName = nameof(SqlMembershipObtainer);
                    var dryRunSettingName = "SqlMembershipObtainer:IsMembershipAggregatorDryRunEnabled";
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                    {
                        var configuration = s.GetService<IConfiguration>();
                        var storageAccountName = configuration["membershipStorageAccountName"];
                        var containerName = configuration["membershipContainerName"];

                        return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                    });

                    services.AddSingleton<IKeyVaultSecret<ISqlMembershipRepository>>(services => new KeyVaultSecret<ISqlMembershipRepository>(CommonServices.GetValueOrDefaultBase("sqlServerMSIConnectionString")));
                    services.AddSingleton<ISqlMembershipRepository, SqlMembershipRepository>();

                    services.AddSingleton<IDataFactorySecret<IDataFactoryRepository>>(new DataFactorySecrets<IDataFactoryRepository>(
                                                                                            CommonServices.GetValueOrDefaultBase("pipeline"),
                                                                                            CommonServices.GetValueOrDefaultBase("dataFactoryName"),
                                                                                            CommonServices.GetValueOrDefaultBase("subscriptionId"),
                                                                                            CommonServices.GetValueOrDefaultBase("dataResourceGroup")));

                    services.AddSingleton<IDataFactoryRepository, DataFactoryRepository>();
                    services.AddGraphAPIClient();
                    services.AddSingleton<IDataFactoryService, DataFactoryService>();
                    services.AddScoped<ISqlMembershipObtainerService, SqlMembershipObtainerService>();
                    services.AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var membershipAggregatorQueue = configuration["serviceBusMembershipAggregatorQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(membershipAggregatorQueue);
                        return new ServiceBusQueueRepository(sender);
                    });

                }).Build();

            host.Run();
        }
    }
}
