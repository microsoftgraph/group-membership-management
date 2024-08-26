// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Hosting;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Repositories.ServiceBusQueue;
using Azure.Identity;
using System;

namespace Hosts.SyncJobUpdater
{

    public class Program
    {
        public static void Main (string[] args)
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
                var functionName = "SyncJobUpdater";
                var dryRunSettingName = string.Empty;
                var rootPath = context.HostingEnvironment.ContentRootPath;
                CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                services.AddScoped(services =>
                {
                    return new SyncJobUpdaterService(
                        services.GetRequiredService<IDatabaseSyncJobsRepository>(),
                        services.GetRequiredService<ILoggingRepository>()
                    );
                });
            })
            .Build();

        host.Run();
        }
    }
}
