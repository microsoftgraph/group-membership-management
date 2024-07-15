// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.JobFinalizer.Startup))]

namespace Hosts.JobFinalizer
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(JobFinalizer);
        protected override string DryRunSettingName => "JobFinalizer:IsDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            .AddScoped<JobFinalizerService>(services =>
            {
                return new JobFinalizerService(
                    services.GetRequiredService<IDatabaseSyncJobsRepository>(),
                    services.GetRequiredService<ILoggingRepository>(),

                );
            })
        }
    }
}
