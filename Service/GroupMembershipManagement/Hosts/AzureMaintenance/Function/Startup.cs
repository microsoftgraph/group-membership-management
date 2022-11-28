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
using Repositories.SyncJobsRepository;
using DIConcreteTypes;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Common.DependencyInjection;
using Microsoft.Graph;
using Repositories.GraphGroups;

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


            builder.Services.AddOptions<SyncJobRepoCredentials<SyncJobRepository>>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ConnectionString = configuration.GetValue<string>("jobsStorageAccountConnectionString");
                settings.TableName = configuration.GetValue<string>("jobsTableName");
            });

            builder.Services.AddSingleton<ISyncJobRepository>(services =>
            {
                var creds = services.GetService<IOptions<SyncJobRepoCredentials<SyncJobRepository>>>();
                return new SyncJobRepository(creds.Value.ConnectionString, creds.Value.TableName, services.GetService<ILoggingRepository>());
            });


            builder.Services.AddSingleton<IKeyVaultSecret<IAzureMaintenanceService>>(services => new KeyVaultSecret<IAzureMaintenanceService>(services.GetService<IOptions<GraphCredentials>>().Value.ClientId))
            .AddSingleton<IGraphServiceClient>((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthProviderFromSecret(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

            builder.Services.AddScoped(services =>
            {
                var tablesToBackupSetting = GetValueOrDefault("maintenanceJobs");
                var tablesToBackup = string.IsNullOrWhiteSpace(tablesToBackupSetting)
                                    ? new List<AzureMaintenanceJob>()
                                    : JsonConvert.DeserializeObject<List<AzureMaintenanceJob>>(tablesToBackupSetting);

                return tablesToBackup;
            });

            builder.Services.AddSingleton<IStorageAccountSecret>(services =>
                new StorageAccountSecret(GetValueOrThrow("jobsStorageAccountConnectionString")));

            builder.Services.AddScoped<IAzureMaintenanceService>(services =>
            {
                return new AzureMaintenanceService(services.GetService<ILoggingRepository>(), services.GetService<IAzureTableBackupRepository>(), services.GetService<IAzureStorageBackupRepository>(), services.GetService<ISyncJobRepository>(), services.GetService<IGraphGroupRepository>(), services.GetService<IEmailSenderRecipient>(), services.GetService<IMailRepository>());
            });
        }
    }
}
