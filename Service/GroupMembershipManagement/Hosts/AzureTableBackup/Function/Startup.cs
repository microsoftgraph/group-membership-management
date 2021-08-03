// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Hosts.AzureTableBackup;
using Services;
using Services.Contracts;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Repositories.AzureTableBackupRepository;
using Repositories.AzureBlobBackupRepository;

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.AzureTableBackup
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(AzureTableBackup);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddScoped<IAzureTableBackupRepository, AzureTableBackupRepository>();
            builder.Services.AddScoped<IAzureStorageBackupRepository, AzureBlobBackupRepository>();
            builder.Services.AddScoped<IAzureTableBackupService>(services =>
             {
                 var tablesToBackupSetting = GetValueOrDefault("tablesToBackup");
                 var tablesToBackup = string.IsNullOrWhiteSpace(tablesToBackupSetting)
                                        ? new List<Services.Entities.AzureTableBackup>()
                                        : JsonConvert.DeserializeObject<List<Services.Entities.AzureTableBackup>>(tablesToBackupSetting);

                 return new AzureTableBackupService(tablesToBackup.ToList<IAzureTableBackup>(), services.GetService<ILoggingRepository>(), services.GetService<IAzureTableBackupRepository>(), services.GetService<IAzureStorageBackupRepository>());
             });
        }
    }
}
