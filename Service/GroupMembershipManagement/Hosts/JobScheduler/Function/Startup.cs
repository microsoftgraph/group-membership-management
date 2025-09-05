// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Hosts.JobScheduler;
using Services;
using Services.Contracts;
using Repositories.Contracts.InjectConfig;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Azure.Identity;
using Azure.Monitor.Query;
using System.Text.Json;
using Azure.Core;

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.JobScheduler
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(JobScheduler);
        protected override string DryRunSettingName => string.Empty;
        protected string JobSchedulerConfigSettingName => "JobScheduler:JobSchedulerConfiguration";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddOptions<JobSchedulerConfigString>().Configure<IConfiguration>((settings, configuration) =>
            {
                if (!string.IsNullOrEmpty(JobSchedulerConfigSettingName))
                {
                    settings.Value = configuration[JobSchedulerConfigSettingName];
                }
            });

            builder.Services.AddScoped<IJobSchedulerConfig>(services =>
            {
                var jsonString = services.GetService<IOptions<JobSchedulerConfigString>>().Value.Value;
                var jobSchedulerConfig = JsonSerializer.Deserialize<JobSchedulerConfig>(jsonString);
                jobSchedulerConfig.WorkspaceId = GetValueOrThrow("logAnalyticsCustomerId");
                return jobSchedulerConfig;
            });

            builder.Services.AddScoped<IRuntimeRetrievalService>(services =>
            {
                TokenCredential credential;
#if DEBUG
                credential = new DefaultAzureCredential();
#else
                credential = new ManagedIdentityCredential();
#endif

                var config = services.GetService<IJobSchedulerConfig>();
                return config.GetRunTimeFromLogs
                ? new LogsRuntimeRetrievalService(config, new LogsQueryClient(credential))
                : new DefaultRuntimeRetrievalService(config.DefaultRuntimeSeconds);
            });

            builder.Services.AddScoped<IJobSchedulingService>(services =>
            {
                return new JobSchedulingService(
                        services.GetService<IDatabaseSyncJobsRepository>(),
                        services.GetService<IRuntimeRetrievalService>(),
                        services.GetService<ILoggingRepository>()
                    );
            });

            builder.Services.AddHttpClient();
        }
    }
}
