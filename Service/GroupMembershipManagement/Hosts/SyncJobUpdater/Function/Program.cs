// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using Azure.Identity;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Repositories.EntityFramework;
using Services.Contracts;
using BusinessLogic.SyncJobUpdater;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.SyncJobUpdater
{
    public class Program
    {
        public static void Main(string[] args)
        {
            const string FunctionName = "SyncJobUpdater";
            const string DryRunSettingName = "SyncJobUpdater:IsDryRunEnabled";

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
                    var rootPath = context.HostingEnvironment.ContentRootPath;

                    CommonServices.ConfigureCommonServices(
                        services,
                        configuration,
                        FunctionName,
                        DryRunSettingName,
                        rootPath);
                    services.ConfigureFunctionsApplicationInsights();

                    services.AddScoped<ISyncJobStatusService, SyncJobStatusService>();
                    services.AddScoped<ISyncJobUpdaterService, SyncJobUpdaterService>();
                })
                .Build();

            host.Run();
        }
    }
}