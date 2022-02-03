// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Entities;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.MembershipDifference;
using Services;
using Services.Contracts;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.GraphUpdater.Startup))]

namespace Hosts.GraphUpdater
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(GraphUpdater);
        protected override string DryRunSettingName => "GraphUpdater:IsGraphUpdaterDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddOptions<ThresholdConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.MaximumNumberOfThresholdRecipients = GetIntSetting(configuration, "GraphUpdater:MaximumNumberOfThresholdRecipients", 10);
                settings.NumberOfThresholdViolationsToNotify = GetIntSetting(configuration, "GraphUpdater:NumberOfThresholdViolationsToNotify", 3);
                settings.NumberOfThresholdViolationsFollowUps = GetIntSetting(configuration, "GraphUpdater:NumberOfThresholdViolationsFollowUps", 3);
                settings.NumberOfThresholdViolationsToDisableJob = GetIntSetting(configuration, "GraphUpdater:NumberOfThresholdViolationsToDisableJob", 10);
            });

            builder.Services.AddSingleton<IThresholdConfig>(services =>
            {
                return new ThresholdConfig
                    (
                        services.GetService<IOptions<ThresholdConfig>>().Value.MaximumNumberOfThresholdRecipients,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToNotify,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsFollowUps,
                        services.GetService<IOptions<ThresholdConfig>>().Value.NumberOfThresholdViolationsToDisableJob
                    );
            });

            builder.Services.AddSingleton<IMembershipDifferenceCalculator<AzureADUser>, MembershipDifferenceCalculator<AzureADUser>>()
            .AddSingleton<IGraphServiceClient>((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthProviderFromSecret(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddSingleton<IGraphGroupRepository, GraphGroupRepository>()
            .AddSingleton<IDeltaCalculatorService, DeltaCalculatorService>()
            .AddSingleton<IGraphUpdaterService, GraphUpdaterService>()
            .AddSingleton<IServiceBusMessageService, ServiceBusMessageService>();
        }

        private int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var isParsed = int.TryParse(configuration[settingName], out var maximumNumberOfThresholdRecipients);
            return isParsed ? maximumNumberOfThresholdRecipients : defaultValue;
        }
    }
}
