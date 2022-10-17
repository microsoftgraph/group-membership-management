// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Hosts.AzureMaintenance;
using Services;
using Services.Contracts;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Repositories.AzureTableBackupRepository;
using Repositories.AzureBlobBackupRepository;
using Repositories.Contracts.AzureMaintenance;
using Repositories.Contracts.InjectConfig;
using Services.Entities;

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.AzureMaintenance
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(AzureMaintenance);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddScoped<IAzureTableBackupRepository, AzureTableBackupRepository>();
            builder.Services.AddScoped<IAzureStorageBackupRepository, AzureBlobBackupRepository>();
            builder.Services.AddScoped(services =>
            {
                var tablesToBackupSetting = GetValueOrDefault("maintenanceJobs");
                var tablesToBackup = string.IsNullOrWhiteSpace(tablesToBackupSetting)
                                    ? new List<AzureMaintenanceJob>()
                                    : JsonConvert.DeserializeObject<List<AzureMaintenanceJob>>(tablesToBackupSetting);

                return tablesToBackup;
            });
            builder.Services.AddScoped<IAzureMaintenanceService>(services =>
            {
                return new AzureMaintenanceService(services.GetService<ILoggingRepository>(), services.GetService<IAzureTableBackupRepository>(), services.GetService<IAzureStorageBackupRepository>());
            });
        }
    }
}
