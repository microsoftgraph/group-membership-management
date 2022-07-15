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
using Services;
using Services.Contracts;

[assembly: FunctionsStartup(typeof(Hosts.MembershipAggregator.Startup))]

namespace Hosts.MembershipAggregator
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(MembershipAggregator);
        protected override string DryRunSettingName => "MembershipAggregator:IsMembershipAggregatorDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddOptions<ThresholdConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.MaximumNumberOfThresholdRecipients = GetIntSetting(configuration, "MembershipAggregator:MaximumNumberOfThresholdRecipients", 10);
                settings.NumberOfThresholdViolationsToNotify = GetIntSetting(configuration, "MembershipAggregator:NumberOfThresholdViolationsToNotify", 3);
                settings.NumberOfThresholdViolationsFollowUps = GetIntSetting(configuration, "MembershipAggregator:NumberOfThresholdViolationsFollowUps", 3);
                settings.NumberOfThresholdViolationsToDisableJob = GetIntSetting(configuration, "MembershipAggregator:NumberOfThresholdViolationsToDisableJob", 10);
            });

            builder.Services.AddSingleton<IGraphServiceClient>((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthProviderFromSecret(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddScoped<IGraphAPIService, GraphAPIService>()
            .AddScoped<IDeltaCalculatorService, DeltaCalculatorService>()
            .AddSingleton<IThresholdConfig>(services =>
            {
                return new ThresholdConfig
                    (
                        services.GetService<IOptions<ThresholdConfig>>().Value.MaximumNumberOfThresholdRecipients,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToNotify,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsFollowUps,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToDisableJob
                    );
            })
            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];

                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            });
        }

        private int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var isParsed = int.TryParse(configuration[settingName], out var maximumNumberOfThresholdRecipients);
            return isParsed ? maximumNumberOfThresholdRecipients : defaultValue;
        }
    }
}
