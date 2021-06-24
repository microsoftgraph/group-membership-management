// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Localization;
using Repositories.Logging;
using Repositories.Mail;
using Repositories.SyncJobsRepository;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Azure.Identity;

namespace Hosts.FunctionBase
{
    public abstract class CommonStartup : FunctionsStartup
    {
        protected abstract string FunctionName { get; }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddLocalization(opts => { opts.ResourcesPath = "Resources"; });
            builder.Services.Configure<RequestLocalizationOptions>(opts =>
            {
                var supportedCultures = new List<CultureInfo>
                        {
                            new CultureInfo("en-US"),
                            new CultureInfo("es-ES"),
                            new CultureInfo("hi-IN")
                        };
                opts.DefaultRequestCulture = new RequestCulture("en-US");
                opts.SupportedCultures = supportedCultures;
                opts.SupportedUICultures = supportedCultures;
            });
            builder.Services.AddSingleton<ILocalizationRepository, LocalizationRepository>();

            builder.Services.AddSingleton<ILogAnalyticsSecret<LoggingRepository>>(new LogAnalyticsSecret<LoggingRepository>(GetValueOrThrow("logAnalyticsCustomerId"), GetValueOrThrow("logAnalyticsPrimarySharedKey"), FunctionName));
            builder.Services.AddSingleton<ILoggingRepository, LoggingRepository>();

            builder.Services.AddOptions<GraphCredentials>().Configure<IConfiguration>((settings, configuration) => configuration.GetSection("graphCredentials").Bind(settings));

            builder.Services.AddOptions<EmailSenderRecipient>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.SenderAddress = configuration.GetValue<string>("senderAddress");
                settings.SenderPassword = configuration.GetValue<string>("senderPassword");
                settings.SyncDisabledCCAddresses = configuration.GetValue<string>("syncDisabledCCEmailAddresses");
                settings.SyncCompletedCCAddresses = configuration.GetValue<string>("syncCompletedCCEmailAddresses");
            });

            builder.Services.AddSingleton<IEmailSenderRecipient>(services =>
            {
                var creds = services.GetService<IOptions<EmailSenderRecipient>>();
                return new EmailSenderRecipient(creds.Value.SenderAddress, creds.Value.SenderPassword, creds.Value.SyncCompletedCCAddresses, creds.Value.SyncDisabledCCAddresses);
            });

            builder.Services.AddSingleton<IMailRepository>(services =>
                new MailRepository(new GraphServiceClient(
                                                    FunctionAppDI.CreateMailAuthProvider(services.GetService<IOptions<GraphCredentials>>().Value)),
                                                    services.GetService<ILocalizationRepository>(),
                                                    services.GetService<ILoggingRepository>()));
            
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddAzureAppConfiguration(options =>
            {
                options.Connect(new System.Uri("https://gmm-appconfiguration-st.azconfig.io"), new DefaultAzureCredential()); //ManagedIdentityCredential
            });
            
            var configurationRoot = configBuilder.Build();

            builder.Services.AddOptions<SyncJobRepoCredentials<SyncJobRepository>>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ConnectionString = configuration.GetValue<string>("jobsStorageAccountConnectionString");
                settings.TableName = configuration.GetValue<string>("jobsTableName");
                settings.GlobalDryRun = configurationRoot["Settings:dryRun"];
            });

            builder.Services.AddSingleton<ISyncJobRepository>(services =>
            {
                var creds = services.GetService<IOptions<SyncJobRepoCredentials<SyncJobRepository>>>();
                return new SyncJobRepository(creds.Value.ConnectionString, creds.Value.TableName, services.GetService<ILoggingRepository>(), creds.Value.GlobalDryRun);
            });
        }

        public string GetValueOrThrow(string key, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            var value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentNullException($"Could not start because of missing configuration option: {key}. Requested by file {callerFile}:{callerLine}.");
            return value;
        }

        public string GetValueOrDefault(string key, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            return Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process) ?? string.Empty;
        }
    }
}
