// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Repositories.ServiceBusQueue;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.SyncJobUpdater.Startup))]

namespace Hosts.SyncJobUpdater
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(SyncJobUpdater);
        protected override string DryRunSettingName => "SyncJobUpdater:IsDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddScoped(services =>
            {
                return new SyncJobUpdaterService(
                    services.GetRequiredService<IDatabaseSyncJobsRepository>(),
                    services.GetRequiredService<ILoggingRepository>()
                );
            });
        }
    }
}
