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
using Repositories.RetryPolicyProvider;
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
using System.IO;
using Models;
using System.Data;

namespace Hosts.FunctionBase
{
    public static class CommonServices
    {
        private const string SCHEMA_DIRECTORY = "Schemas";

        public static void ConfigureCommonServices(IServiceCollection services, IConfiguration configuration, string functionName, string dryRunSettingName, string rootPath)
        {
            services.AddAzureAppConfiguration();
            services.AddFeatureManagement();
            services.AddScoped<IFeatureFlagRepository, FeatureFlagRepository>();

            services.AddLocalization(opts => { opts.ResourcesPath = "Resources"; });
            services.Configure<RequestLocalizationOptions>(opts =>
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

            services.AddOptions<DryRunValue>().Configure<IConfiguration>((settings, configuration) =>
            {
                if (!string.IsNullOrEmpty(dryRunSettingName))
                {
                    var checkParse = bool.TryParse(configuration[dryRunSettingName], out bool value);
                    if (checkParse)
                        settings.DryRunEnabled = value;
                }

            });

            services.AddSingleton<IDryRunValue>(services =>
            {
                return new DryRunValue(services.GetService<IOptions<DryRunValue>>().Value.DryRunEnabled);
            });

            services.AddSingleton<ILocalizationRepository, LocalizationRepository>();

            services.AddSingleton<ILogAnalyticsSecret<LoggingRepository>>(new LogAnalyticsSecret<LoggingRepository>(GetValueOrThrowBase("logAnalyticsCustomerId"), GetValueOrThrowBase("logAnalyticsPrimarySharedKey"), functionName));
            services.AddOptions<AppConfigVerbosity>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.Verbosity = configuration.GetValue<VerbosityLevel>("GMM:LoggingVerbosity");
            });

            services.AddDbContext<GMMContext>(options =>
                options.UseSqlServer(GetValueOrThrowBase("ConnectionStrings:JobsContext")),
                ServiceLifetime.Scoped
            );

            services.AddDbContext<GMMReadContext>(options =>
                options.UseSqlServer(GetValueOrThrowBase("ConnectionStrings:JobsContextReadOnly")),
                ServiceLifetime.Scoped
            );

            services.AddOptions<GraphServiceAttemptsValue>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.MaxRetryAfterAttempts = GetIntSettingBase(configuration, "MaxRetryAfterAttempts", 4);
                settings.MaxExceptionHandlingAttempts = GetIntSettingBase(configuration, "MaxExceptionHandlingAttempts", 2);
            });

            services.AddSingleton<IGraphServiceAttemptsValue>(services =>
            {
                var options = services.GetRequiredService<IOptions<GraphServiceAttemptsValue>>();
                return new GraphServiceAttemptsValue
                {
                    MaxRetryAfterAttempts = options.Value.MaxRetryAfterAttempts,
                    MaxExceptionHandlingAttempts = options.Value.MaxExceptionHandlingAttempts
                };
            });

            services.AddSingleton<ILoggingRepository, LoggingRepository>();
            services.AddScoped<IDatabaseSyncJobsRepository, DatabaseSyncJobsRepository>();
            services.AddScoped<IDatabasePendingSyncJobChangesRepository, DatabasePendingSyncJobChangesRepository>();
            services.AddScoped<IDatabaseSettingsRepository, DatabaseSettingsRepository>();
            services.AddScoped<IDatabaseDestinationAttributesRepository, DatabaseDestinationAttributesRespository>();
            services.AddScoped<INotificationTypesRepository, NotificationTypesRepository>();
            services.AddScoped<IJobNotificationsRepository, JobNotificationRepository>();
            services.AddScoped<IRetryPolicyProvider, RetryPolicyProvider>();
            services.AddSingleton<IAppConfigVerbosity>(services =>
            {
                var creds = services.GetService<IOptions<AppConfigVerbosity>>();
                return new AppConfigVerbosity(creds.Value.Verbosity);
            });

            services.AddOptions<GMMResources>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.LearnMoreAboutGMMUrl = configuration.GetValue<string>("GMM:LearnMoreUrl");
            });

            services.AddSingleton<IGMMResources>(services =>
            {
                var creds = services.GetService<IOptions<GMMResources>>();
                return new GMMResources(creds.Value.LearnMoreAboutGMMUrl);
            });

            services.AddOptions<GraphCredentials>()
                            .Configure<IConfiguration>((settings, configuration) =>
                            {
                                configuration.GetSection("graphCredentials").Bind(settings);
                                var authenticationType = Common.DependencyInjection.ServiceCollectionExtensions.MapStringToAuthenticationType(configuration["GraphAPI:AuthenticationType"]);
                                settings.AuthenticationType = authenticationType;
                            });

            services.AddOptions<EmailSenderRecipient>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.SenderAddress = configuration.GetValue<string>("senderAddress");
                settings.SenderPassword = configuration.GetValue<string>("senderPassword");
                settings.SyncDisabledCCAddresses = configuration.GetValue<string>("syncDisabledCCEmailAddresses");
                settings.SyncCompletedCCAddresses = configuration.GetValue<string>("syncCompletedCCEmailAddresses");
                settings.SupportEmailAddresses = configuration.GetValue<string>("supportEmailAddresses");
            });

            services.AddSingleton<IEmailSenderRecipient>(services =>
            {
                var creds = services.GetService<IOptions<EmailSenderRecipient>>();
                return new EmailSenderRecipient(
                    creds.Value.SenderAddress,
                    creds.Value.SenderPassword,
                    creds.Value.SyncCompletedCCAddresses,
                    creds.Value.SyncDisabledCCAddresses,
                    creds.Value.SupportEmailAddresses);
            });

            services.AddSingleton<IMailConfig>(services =>
            {
                var configuration = services.GetService<IConfiguration>();
                return new MailConfig(configuration.GetValue<bool>("Mail:IsAdaptiveCardEnabled"),
                    configuration.GetValue("Mail:IsMailApplicationPermissionGranted", false),
                    configuration.GetValue<string>("senderAddress"),
                    configuration.GetValue("Mail:SkipMailNotifications", false));
            });

            services.AddScoped<IMailRepository>(services =>
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
                        GetValueOrDefaultBase("actionableEmailProviderId"),
                        services.GetService<IGraphGroupRepository>(),
                        services.GetService<IDatabaseSettingsRepository>(),
                        services.GetService<IRetryPolicyProvider>()
                        );
            });

            services.AddScoped<INotificationRepository, NotificationRepository>();

            services.AddOptions<ThresholdNotificationConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.IsThresholdNotificationEnabled = configuration.GetValue<bool>("ThresholdNotification:IsThresholdNotificationEnabled");
            });
            services.AddSingleton<IThresholdNotificationConfig>(services =>
            {
                var creds = services.GetService<IOptions<ThresholdNotificationConfig>>();
                return new ThresholdNotificationConfig(creds.Value.IsThresholdNotificationEnabled);
            });

            services.AddSingleton(sp =>
            {
                var telemetryConfiguration = new TelemetryConfiguration();
                telemetryConfiguration.InstrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
                telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
                var tc = new TelemetryClient(telemetryConfiguration);
                tc.Context.Operation.Name = functionName;
                return tc;
            });

            services.AddSingleton(services =>
            {
                var serviceBusFQN = GetValueOrDefaultBase("gmmServiceBus__fullyQualifiedNamespace");

                if (string.IsNullOrWhiteSpace(serviceBusFQN))
                    throw new ArgumentNullException($"Could not start because of missing configuration option: servicebus fully qualified namespace.");

                return new ServiceBusClient(serviceBusFQN, new DefaultAzureCredential());
            });

            var jsonSchemasPath = Path.Combine(rootPath, SCHEMA_DIRECTORY);
            var schemaProvider = new SchemaProvider();
            if (Directory.Exists(jsonSchemasPath))
            {
                var files = Directory.EnumerateFiles(jsonSchemasPath);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    schemaProvider.Schemas.Add((Schema)Enum.Parse(typeof(Schema), fileName), File.ReadAllText(file));
                }
            }
            services.AddSingleton(schemaProvider);
        }

        public static string GetValueOrThrowBase(string key, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            var value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentNullException($"Could not start because of missing configuration option: {key}. Requested by file {callerFile}:{callerLine}.");
            return value;
        }

        public static string GetValueOrDefaultBase(string key, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            return Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process) ?? string.Empty;
        }
        public static int GetIntSettingBase(IConfiguration configuration, string settingName, int defaultValue)
        {
            var checkParse = int.TryParse(configuration[settingName], out int value);
            return checkParse ? value : defaultValue;
        }

        public static bool GetBoolSettingBase(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            if (checkParse)
                return value;
            return defaultValue;
        }
    }
}
