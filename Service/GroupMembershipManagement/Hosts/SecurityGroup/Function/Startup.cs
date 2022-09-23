// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.SecurityGroup.Startup))]

namespace Hosts.SecurityGroup
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(SecurityGroup);
        protected override string DryRunSettingName => "SecurityGroup:IsSecurityGroupDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddOptions<DeltaCachingConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.DeltaCacheEnabled = GetBoolSetting(configuration, "SecurityGroup:IsDeltaCacheEnabled", false);
            });
            builder.Services.AddSingleton<IDeltaCachingConfig>(services =>
            {
                return new DeltaCachingConfig(services.GetService<IOptions<DeltaCachingConfig>>().Value.DeltaCacheEnabled);
            });

            builder.Services.AddSingleton<IGraphServiceClient>((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthProviderFromSecret(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddScoped<SGMembershipCalculator>()
            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];

                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            });
        }
        private bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            if (checkParse)
                return value;
            return defaultValue;
        }
    }
}
