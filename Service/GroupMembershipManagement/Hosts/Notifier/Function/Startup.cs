// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Hosts.Notifier;
using Services;
using Services.Contracts;
using Repositories.Contracts.InjectConfig;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Azure.Identity;
using Azure.Monitor.Query;

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

            builder.Services.AddOptions<NotifierConfigString>().Configure<IConfiguration>((settings, configuration) =>
            {
                if (!string.IsNullOrEmpty(NotifierConfigSettingName))
                {
                    settings.Value = configuration[NotifierConfigSettingName];
                }
            });

            builder.Services.AddScoped<INotifierConfig>(services =>
            {
                var jsonString = services.GetService<IOptions<NotifierConfigString>>().Value.Value;
                var notifierConfig = JsonConvert.DeserializeObject<NotifierConfig>(jsonString);
                notifierConfig.WorkspaceId = GetValueOrThrow("logAnalyticsCustomerId");
                return notifierConfig;
            });

            builder.Services.AddScoped<INotifierService>(services =>
            {
                return new NotifierService(
                    services.GetService<ILoggingRepository>(),
                    services.GetService<IMailRepository>(),
                    services.GetService<IEmailSenderRecipient>());
            });

            builder.Services.AddHttpClient();
        }
    }
}
