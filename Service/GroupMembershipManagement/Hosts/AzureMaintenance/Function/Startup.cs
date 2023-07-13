// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Hosts.AzureMaintenance;
using Services;
using Services.Contracts;
using Repositories.Contracts.InjectConfig;
using DIConcreteTypes;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Common.DependencyInjection;
using Microsoft.Graph;
using Repositories.GraphGroups;
using Repositories.EntityFramework;

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

            builder.Services.AddScoped<IDatabasePurgedSyncJobsRepository, DatabasePurgedSyncJobsRepository>();

            builder.Services.AddOptions<HandleInactiveJobsConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.HandleInactiveJobsEnabled = GetBoolSetting(configuration, "AzureMaintenance:HandleInactiveJobsEnabled", false);
                settings.NumberOfDaysBeforeDeletion = GetIntSetting(configuration, "AzureMaintenance:NumberOfDaysBeforeDeletion", 0);
            });
            builder.Services.AddSingleton<IHandleInactiveJobsConfig>(services =>
            {
                return new HandleInactiveJobsConfig(
                    services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.HandleInactiveJobsEnabled,
                    services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.NumberOfDaysBeforeDeletion);
            });

            builder.Services.AddSingleton<IKeyVaultSecret<IAzureMaintenanceService>>(services => new KeyVaultSecret<IAzureMaintenanceService>(services.GetService<IOptions<GraphCredentials>>().Value.ClientId))
            .AddSingleton((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

            builder.Services.AddScoped<IAzureMaintenanceService>(services =>
            {
                return new AzureMaintenanceService(services.GetService<IDatabaseSyncJobsRepository>(),
                    services.GetService<IDatabasePurgedSyncJobsRepository>(),
                    services.GetService<IGraphGroupRepository>(),
                    services.GetService<IEmailSenderRecipient>(),
                    services.GetService<IMailRepository>(),
                    services.GetService<IHandleInactiveJobsConfig>());
            });
        }

        private bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            if (checkParse)
                return value;
            return defaultValue;
        }

        private int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var checkParse = int.TryParse(configuration[settingName], out int value);
            if (checkParse)
                return value;
            return defaultValue;
        }
    }
}
