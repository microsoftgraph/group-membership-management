// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Hosting;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.ServiceBusQueue;
using Services;
using Services.Contracts;
using System;
using Azure.Identity;
using Repositories.EntityFramework;
using Repositories.NotificationsRepository;
using Repositories.GraphAzureADUsers;

namespace Hosts.AzureUserReader
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
                var functionName = "AzureUserReader";
                var dryRunSettingName = string.Empty;
                var rootPath = context.HostingEnvironment.ContentRootPath;
                CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
                services.AddGraphAPIClient();

                services.AddSingleton<IStorageAccountSecret>(services =>
                    new StorageAccountSecret(CommonServices.GetValueOrThrowBase("storageAccountConnectionString")));

                services.AddScoped<IGraphUserRepository, GraphUserRepository>();
                services.AddScoped<IBlobClientFactory, BlobClientFactory>();
                services.AddScoped<IAzureUserReaderService, AzureUserReaderService>();
            })
            .Build();
            host.Run();
        }
    }
}

