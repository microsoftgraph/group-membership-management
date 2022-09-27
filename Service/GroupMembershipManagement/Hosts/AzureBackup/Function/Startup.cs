// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Hosts.AzureBackup;
using Services;
using Services.Contracts;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Repositories.AzureBackupRepository;
using Repositories.AzureBlobBackupRepository;

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.AzureBackup
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(AzureBackup);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddScoped<IAzureBackupRepository, AzureBackupRepository>();
            builder.Services.AddScoped<IAzureStorageBackupRepository, AzureBlobBackupRepository>();
            builder.Services.AddScoped<IAzureBackupService>(services =>
            {
                var tablesToBackupSetting = GetValueOrDefault("tablesToBackup");
                var tablesToBackup = string.IsNullOrWhiteSpace(tablesToBackupSetting)
                                    ? new List<Services.Entities.AzureBackup>()
                                    : JsonConvert.DeserializeObject<List<Services.Entities.AzureBackup>>(tablesToBackupSetting);

                return new AzureBackupService(tablesToBackup, services.GetService<ILoggingRepository>(), services.GetService<IAzureBackupRepository>(), services.GetService<IAzureStorageBackupRepository>());
            });
        }
    }
}
