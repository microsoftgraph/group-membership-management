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

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.JobScheduler
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(JobScheduler);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddScoped<IRuntimeRetrievalService>(services =>
            {
                return new DefaultRuntimeRetrievalService(0); // TODO
            });

            builder.Services.AddScoped<IJobSchedulingService>(services =>
            {
                return new JobSchedulingService(
                        0, // TODO
                        0, // TODO
                        services.GetService<ISyncJobRepository>(),
                        services.GetService<IRuntimeRetrievalService>(),
                        services.GetService<ILoggingRepository>()
                    );
            });

            builder.Services.AddScoped<IJobSchedulerConfig>(services =>
            {
                return new JobSchedulerConfig(false, 0, false, false); // TODO
            });

            builder.Services.AddScoped<IJobSchedulerApplicationService>(services =>
            {
                return new ApplicationService(services.GetService<IJobSchedulingService>(), services.GetService<IJobSchedulerConfig>());
            });
        }
    }
}
