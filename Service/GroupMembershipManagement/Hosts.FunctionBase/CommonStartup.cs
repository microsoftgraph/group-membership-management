// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Identity;
using Common.DependencyInjection;
using DIConcreteTypes;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
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
using Repositories.NotificationsRepository;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.FeatureManagement;
using Azure.Messaging.ServiceBus;
using Repositories.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using Repositories.EntityFramework;
using Repositories.FeatureFlag;
using Azure.Core;

namespace Hosts.FunctionBase
{
    public abstract class CommonStartup : FunctionsStartup
    {
        protected abstract string FunctionName { get; }
        protected abstract string DryRunSettingName { get; }

        public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
        {
            builder.ConfigurationBuilder.AddAzureAppConfiguration(options =>
            {
                options.Connect(new Uri(GetValueOrThrow("appConfigurationEndpoint")), new DefaultAzureCredential())
                       .UseFeatureFlags();
            });
        }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddAzureAppConfiguration();
            builder.Services.AddFeatureManagement();
            builder.Services.AddScoped<IFeatureFlagRepository, FeatureFlagRepository>();

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

            builder.Services.AddOptions<DryRunValue>().Configure<IConfiguration>((settings, configuration) =>
            {
                if (!string.IsNullOrEmpty(DryRunSettingName))
                {
                    var checkParse = bool.TryParse(configuration[DryRunSettingName], out bool value);
                    if (checkParse)
                        settings.DryRunEnabled = value;
                }

            });

            builder.Services.AddSingleton<IDryRunValue>(services =>
            {
                return new DryRunValue(services.GetService<IOptions<DryRunValue>>().Value.DryRunEnabled);
            });

            builder.Services.AddSingleton<ILocalizationRepository, LocalizationRepository>();

            builder.Services.AddSingleton<ILogAnalyticsSecret<LoggingRepository>>(new LogAnalyticsSecret<LoggingRepository>(GetValueOrThrow("logAnalyticsCustomerId"), GetValueOrThrow("logAnalyticsPrimarySharedKey"), FunctionName));
            builder.Services.AddOptions<AppConfigVerbosity>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.Verbosity = configuration.GetValue<VerbosityLevel>("GMM:LoggingVerbosity");
            });

            builder.Services.AddDbContext<GMMContext>(options =>
                options.UseSqlServer(GetValueOrThrow("ConnectionStrings:JobsContext")),
                ServiceLifetime.Scoped
            );

            builder.Services.AddDbContext<GMMReadContext>(options =>
                options.UseSqlServer(GetValueOrThrow("ConnectionStrings:JobsContextReadOnly")),
                ServiceLifetime.Scoped
            );

            builder.Services.AddScoped<IDatabaseSyncJobsRepository, DatabaseSyncJobsRepository>();
            builder.Services.AddScoped<IDatabaseSettingsRepository, DatabaseSettingsRepository>();
            builder.Services.AddScoped<IDatabaseDestinationAttributesRepository, DatabaseDestinationAttributesRespository>();
            builder.Services.AddScoped<INotificationTypesRepository, NotificationTypesRepository>();
            builder.Services.AddScoped<IJobNotificationsRepository, JobNotificationRepository>();

            builder.Services.AddSingleton<IAppConfigVerbosity>(services =>
            {
                var creds = services.GetService<IOptions<AppConfigVerbosity>>();
                return new AppConfigVerbosity(creds.Value.Verbosity);
            });
            builder.Services.AddSingleton<ILoggingRepository, LoggingRepository>();

            builder.Services.AddOptions<GMMResources>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.LearnMoreAboutGMMUrl = configuration.GetValue<string>("GMM:LearnMoreUrl");
            });

            builder.Services.AddSingleton<IGMMResources>(services =>
            {
                var creds = services.GetService<IOptions<GMMResources>>();
                return new GMMResources(creds.Value.LearnMoreAboutGMMUrl);
            });

            builder.Services.AddOptions<GraphCredentials>()
                            .Configure<IConfiguration>((settings, configuration) =>
                            {
                                configuration.GetSection("graphCredentials").Bind(settings);
                                var authenticationType = Common.DependencyInjection.ServiceCollectionExtensions.MapStringToAuthenticationType(configuration["GraphAPI:AuthenticationType"]);
                                settings.AuthenticationType = authenticationType;
                            });

            builder.Services.AddOptions<EmailSenderRecipient>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.SenderAddress = configuration.GetValue<string>("senderAddress");
                settings.SenderPassword = configuration.GetValue<string>("senderPassword");
                settings.SyncDisabledCCAddresses = configuration.GetValue<string>("syncDisabledCCEmailAddresses");
                settings.SyncCompletedCCAddresses = configuration.GetValue<string>("syncCompletedCCEmailAddresses");
                settings.SupportEmailAddresses = configuration.GetValue<string>("supportEmailAddresses");
            });

            builder.Services.AddSingleton<IEmailSenderRecipient>(services =>
            {
                var creds = services.GetService<IOptions<EmailSenderRecipient>>();
                return new EmailSenderRecipient(
                    creds.Value.SenderAddress,
                    creds.Value.SenderPassword,
                    creds.Value.SyncCompletedCCAddresses,
                    creds.Value.SyncDisabledCCAddresses,
                    creds.Value.SupportEmailAddresses);
            });

            builder.Services.AddSingleton<IMailConfig>(services =>
            {
                var configuration = services.GetService<IConfiguration>();
                return new MailConfig(configuration.GetValue<bool>("Mail:IsAdaptiveCardEnabled"),
                    configuration.GetValue("Mail:IsMailApplicationPermissionGranted", false),
                    configuration.GetValue<string>("senderAddress"));
            });

            builder.Services.AddSingleton<IMailRepository>(services =>
            {
                var mailConfig = services.GetService<IMailConfig>();
                var graphCredentials = services.GetService<IOptions<GraphCredentials>>().Value;

                TokenCredential graphTokenCredential;

                if (mailConfig.GMMHasSendMailApplicationPermissions)
                {
                    graphTokenCredential = FunctionAppDI.CreateAuthProviderFromSecret(graphCredentials);
                }
                else
                {
                    var mailCredentials = services.GetService<IOptions<EmailSenderRecipient>>();
                    graphCredentials.ServiceAccountUserName = mailCredentials.Value.SenderAddress;
                    graphCredentials.ServiceAccountPassword = mailCredentials.Value.SenderPassword;

                    graphTokenCredential = FunctionAppDI.CreateServiceAccountAuthProvider(graphCredentials);
                }

                return new MailRepository(
                    new GraphServiceClient(graphTokenCredential),
                        services.GetService<IMailConfig>(),
                        services.GetService<ILocalizationRepository>(),
                        services.GetService<ILoggingRepository>(),
                        GetValueOrDefault("actionableEmailProviderId"),
                        services.GetService<IGraphGroupRepository>(),
                        services.GetService<IDatabaseSettingsRepository>());
            });

            builder.Services.AddOptions<NotificationRepoCredentials<NotificationRepository>>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ConnectionString = configuration.GetValue<string>("jobsStorageAccountConnectionString");
                settings.TableName = configuration.GetValue<string>("notificationsTableName");
            });

            builder.Services.AddSingleton<INotificationRepository, NotificationRepository>();

            builder.Services.AddOptions<ThresholdNotificationConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.IsThresholdNotificationEnabled = configuration.GetValue<bool>("ThresholdNotification:IsThresholdNotificationEnabled");
            });
            builder.Services.AddSingleton<IThresholdNotificationConfig>(services =>
            {
                var creds = services.GetService<IOptions<ThresholdNotificationConfig>>();
                return new ThresholdNotificationConfig(creds.Value.IsThresholdNotificationEnabled);
            });

            builder.Services.AddSingleton(sp =>
            {
                var telemetryConfiguration = new TelemetryConfiguration();
                telemetryConfiguration.InstrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
                telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
                var tc = new TelemetryClient(telemetryConfiguration);
                tc.Context.Operation.Name = FunctionName;
                return tc;
            });

            builder.Services.AddSingleton(services =>
            {
                var serviceBusFQN = GetValueOrDefault("gmmServiceBus__fullyQualifiedNamespace");

                if (string.IsNullOrWhiteSpace(serviceBusFQN))
                    throw new ArgumentNullException($"Could not start because of missing configuration option: servicebus fully qualified namespace.");

                return new ServiceBusClient(serviceBusFQN, new DefaultAzureCredential());
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
