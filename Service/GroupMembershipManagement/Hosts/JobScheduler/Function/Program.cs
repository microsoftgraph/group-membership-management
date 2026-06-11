using Azure.Identity;
using Azure.Monitor.Query;
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services;
using Services.Contracts;
using System;
using System.Text.Json;

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
                    var functionName = "JobScheduler";
                    var dryRunSettingName = string.Empty;
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    var jobSchedulerConfigSettingName = "JobScheduler:JobSchedulerConfiguration";

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
                        var jobSchedulerConfig = JsonSerializer.Deserialize<JobSchedulerConfig>(jsonString);
                        jobSchedulerConfig.WorkspaceId = CommonServices.GetValueOrThrowBase(configuration, "logAnalyticsCustomerId");
                        return jobSchedulerConfig;
                    });

                    services.AddScoped<IRuntimeRetrievalService>(services =>
                    {
                        DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);

                        var config = services.GetService<IJobSchedulerConfig>();
                        return config.GetRunTimeFromLogs
                        ? new LogsRuntimeRetrievalService(config, new LogsQueryClient(credential))
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
