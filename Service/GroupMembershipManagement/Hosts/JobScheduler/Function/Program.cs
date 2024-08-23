// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Azure.Monitor.Query;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services;
using Services.Contracts;
using System;

namespace Hosts.JobScheduler
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
                    var jobSchedulerConfigSettingName = "JobScheduler:JobSchedulerConfiguration";
                    var configuration = context.Configuration;
                    var functionName = nameof(JobScheduler);
                    var dryRunSettingName = string.Empty;
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.AddOptions<JobSchedulerConfigString>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        if (!string.IsNullOrEmpty(jobSchedulerConfigSettingName))
                        {
                            settings.Value = configuration[jobSchedulerConfigSettingName];
                        }
                    });

                    services.AddScoped<IJobSchedulerConfig>(services =>
                    {
                        var jsonString = services.GetService<IOptions<JobSchedulerConfigString>>().Value.Value;
                        var jobSchedulerConfig = JsonConvert.DeserializeObject<JobSchedulerConfig>(jsonString);
                        jobSchedulerConfig.WorkspaceId = CommonServices.GetValueOrThrowBase("logAnalyticsCustomerId");
                        return jobSchedulerConfig;
                    });

                    services.AddScoped<IRuntimeRetrievalService>(services =>
                    {
                        var config = services.GetService<IJobSchedulerConfig>();
                        return config.GetRunTimeFromLogs
                        ? new LogsRuntimeRetrievalService(config, new LogsQueryClient(new DefaultAzureCredential()))
                        : new DefaultRuntimeRetrievalService(config.DefaultRuntimeSeconds);
                    });

                    services.AddScoped<IJobSchedulingService>(services =>
                    {
                        return new JobSchedulingService(
                                services.GetService<IDatabaseSyncJobsRepository>(),
                                services.GetService<IRuntimeRetrievalService>(),
                                services.GetService<ILoggingRepository>()
                            );
                    });

                    services.AddHttpClient();

                }).Build();

            host.Run();
        }
    }
}

