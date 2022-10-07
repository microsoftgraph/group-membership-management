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
using Repositories.AzureMaintenanceRepository;
using Repositories.AzureBlobBackupRepository;

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

            builder.Services.AddScoped<IAzureMaintenanceRepository, AzureMaintenanceRepository>();
            builder.Services.AddScoped<IAzureStorageBackupRepository, AzureBlobBackupRepository>();
            builder.Services.AddScoped<IAzureMaintenanceService>(services =>
            {
                var tablesToBackupSetting = GetValueOrDefault("tablesToBackup");
                var tablesToBackup = string.IsNullOrWhiteSpace(tablesToBackupSetting)
                                    ? new List<Services.Entities.AzureMaintenance>()
                                    : JsonConvert.DeserializeObject<List<Services.Entities.AzureMaintenance>>(tablesToBackupSetting);

                return new AzureMaintenanceService(tablesToBackup, services.GetService<ILoggingRepository>(), services.GetService<IAzureMaintenanceRepository>(), services.GetService<IAzureStorageBackupRepository>());
            });
        }
    }
}
