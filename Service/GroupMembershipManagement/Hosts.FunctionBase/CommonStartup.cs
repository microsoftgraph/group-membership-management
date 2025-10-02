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
    public abstract class CommonStartup : FunctionsStartup
    {
        protected abstract string FunctionName { get; }
        protected abstract string DryRunSettingName { get; }

        private const string SCHEMA_DIRECTORY = "Schemas";

        public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
        {
            builder.ConfigurationBuilder.AddAzureAppConfiguration(options =>
            {
                DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
                options.Connect(new Uri(GetValueOrThrow("appConfigurationEndpoint")), credential)
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
                options.UseSqlServer(GetValueOrThrow("ConnectionStrings:JobsContext"), sqlServerOptions =>
                {
                    sqlServerOptions.EnableRetryOnFailure();
                }),
                ServiceLifetime.Scoped
            );

            builder.Services.AddDbContext<GMMReadContext>(options =>
                options.UseSqlServer(GetValueOrThrow("ConnectionStrings:JobsContextReadOnly"), sqlServerOptions =>
                {
                    sqlServerOptions.EnableRetryOnFailure();
                }),
                ServiceLifetime.Scoped
            );

            builder.Services.AddOptions<GraphServiceAttemptsValue>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.MaxRetryAfterAttempts = GetIntSetting(configuration, "MaxRetryAfterAttempts", 4);
                settings.MaxExceptionHandlingAttempts = GetIntSetting(configuration, "MaxExceptionHandlingAttempts", 2);
            });

            builder.Services.AddSingleton<IGraphServiceAttemptsValue>(services =>
            {
                var options = services.GetRequiredService<IOptions<GraphServiceAttemptsValue>>();
                return new GraphServiceAttemptsValue
                {
                    MaxRetryAfterAttempts = options.Value.MaxRetryAfterAttempts,
                    MaxExceptionHandlingAttempts = options.Value.MaxExceptionHandlingAttempts
                };
            });

            builder.Services.AddSingleton<ILoggingRepository, LoggingRepository>();
            builder.Services.AddScoped<IDatabaseSyncJobsRepository, DatabaseSyncJobsRepository>();
            builder.Services.AddScoped<IDatabaseGroupsRepository, DatabaseGroupsRepository>();
            builder.Services.AddScoped<IDatabaseChannelsRepository, DatabaseChannelsRepository>();
            builder.Services.AddScoped<IDatabaseSettingsRepository, DatabaseSettingsRepository>();
            builder.Services.AddScoped<IDatabaseDestinationAttributesRepository, DatabaseDestinationAttributesRespository>();
            builder.Services.AddScoped<INotificationTypesRepository, NotificationTypesRepository>();
            builder.Services.AddScoped<IJobNotificationsRepository, JobNotificationRepository>();
            builder.Services.AddScoped<IRetryPolicyProvider, RetryPolicyProvider>();
            builder.Services.AddSingleton<IAppConfigVerbosity>(services =>
            {
                var creds = services.GetService<IOptions<AppConfigVerbosity>>();
                return new AppConfigVerbosity(creds.Value.Verbosity);
            });

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

            builder.Services.AddScopedMailRepository();

            builder.Services.AddScoped<INotificationRepository, NotificationRepository>();

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

                DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
                return new ServiceBusClient(serviceBusFQN, credential);
            });

            var rootPath = builder.GetContext().ApplicationRootPath;
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
            builder.Services.AddSingleton(schemaProvider);
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
        private int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var checkParse = int.TryParse(configuration[settingName], out int value);
            return checkParse ? value : defaultValue;
        }
    }
}
