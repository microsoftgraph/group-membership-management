// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Hosts.Notifier;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Services.Notifier;
using Services.Notifier.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Microsoft.Extensions.Configuration;
using DIConcreteTypes;
using Services.Notifications;
using Services.Contracts.Notifications;
using System;

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.Notifier
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(Notifier);
        protected override string DryRunSettingName => string.Empty;
        protected string NotifierConfigSettingName => "Notifier:NotifierConfiguration";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddLocalization(options =>
            {
                options.ResourcesPath = "Resources";
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddScoped<INotifierService, NotifierService>();

            builder.Services.AddOptions<ThresholdNotificationServiceConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ActionableEmailProviderId = configuration.GetValue<Guid>("actionableEmailProviderId");
                settings.ApiHostname = configuration.GetValue<string>("apiHostname");
            });
            builder.Services.AddScoped<IThresholdNotificationConfig>((sp) =>
            {
                return new ThresholdNotificationConfig(true);
            });
            builder.Services.AddScoped<IThresholdNotificationService, ThresholdNotificationService>();

            builder.Services.AddHttpClient();
        }
    }
}
