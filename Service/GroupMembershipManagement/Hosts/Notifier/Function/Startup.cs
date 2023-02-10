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
using Common.DependencyInjection;
using DIConcreteTypes;
using Microsoft.Graph;

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

            builder.Services.AddSingleton<IKeyVaultSecret<INotifierService>>(services => new KeyVaultSecret<INotifierService>(services.GetService<IOptions<GraphCredentials>>().Value.ClientId))
            .AddSingleton<IGraphServiceClient>((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<INotifierService>(services =>
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
