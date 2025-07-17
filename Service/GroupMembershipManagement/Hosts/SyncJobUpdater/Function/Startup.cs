// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Repositories.EntityFramework;
using Services.Contracts;

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

            builder.Services.AddScoped<ISyncJobHistoryRepository, SyncJobHistoryRepository>();
            builder.Services.AddScoped<ISyncJobUpdaterService>(services =>
            {
                return new SyncJobUpdaterService(
                    services.GetRequiredService<IDatabaseSyncJobsRepository>(),
                    services.GetRequiredService<ILoggingRepository>(),
                    services.GetRequiredService<ISyncJobHistoryRepository>()
                );
            });
        }
    }
}
