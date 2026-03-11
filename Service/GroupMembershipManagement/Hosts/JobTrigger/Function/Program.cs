// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.EntityFramework;
using Repositories.ServiceBusQueue;
using Repositories.ServiceBusTopics;
using Repositories.TeamsChannel;
using Services;
using Services.Contracts;
using BusinessLogic.SyncJobUpdater;
using System;
using System.IO;

namespace Hosts.JobTrigger
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureAppConfiguration((context, config) =>
                {
                    var settings = config.Build();
                    var appConfigEndpoint = CommonServices.GetValueOrThrowBase(settings, "appConfigurationEndpoint");

                    config.AddAzureAppConfiguration(options =>
                    {
                        DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
                        options.Connect(new Uri(appConfigEndpoint), credential).UseFeatureFlags();
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    var functionName = "JobTrigger";
                    var dryRunSettingName = string.Empty;
                    var rootPath = context.HostingEnvironment.ContentRootPath;
                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);

                    services.AddOptions<JobTriggerConfig>().Configure<IConfiguration>((settings, configuration) =>
                    {                       
                        settings.GMMHasGroupReadWriteAllPermissions = GetBoolSetting(configuration, "JobTrigger:IsGroupReadWriteAllGranted", false);
                        settings.GMMHasChannelReadWriteAllPermissions = GetBoolSetting(configuration, "TeamsChannel:IsChannelReadWriteApplicationPermissionGranted", false);
                        settings.JobCountThreshold = GetIntSetting(configuration, "JobTrigger:JobCountThreshold", 10);
                        settings.JobPerMilleThreshold = GetIntSetting(configuration, "JobTrigger:JobPerMilleThreshold", 10);
                    });

                    services.AddSingleton<IJobTriggerConfig>(services => services.GetService<IOptions<JobTriggerConfig>>().Value);

                    services.AddSingleton<IKeyVaultSecret<IJobTriggerService>>(services =>
                    {
                        var graphCredentials = services.GetService<IOptions<GraphCredentials>>().Value;
                        return new KeyVaultSecret<IJobTriggerService>(graphCredentials.GMMOwnerAppId);
                    })
                    .AddSingleton<IKeyVaultSecret<IJobTriggerService, Guid>>(services =>
                    {
                        var configuration = services.GetService<IConfiguration>();
                        var serviceAccountObjectId = string.IsNullOrWhiteSpace(configuration["teamsChannelServiceAccountObjectId"]) ? Guid.Empty : Guid.Parse(configuration["teamsChannelServiceAccountObjectId"]);
                        return new KeyVaultSecret<IJobTriggerService, Guid>(serviceAccountObjectId);
                    });

                    services.AddGraphAPIClient();
                    services.AddScoped<IGraphGroupRepository, GraphGroupRepository>();

                    services.Configure<GraphCredentials>("TeamsGraphCredentials", configuration.GetSection("TeamsGraphCredentials"));

                    services.AddTransient<ITeamsChannelRepository>((services) =>
                    {
                        var configuration = services.GetService<IConfiguration>();
                        var enableTeamsChannel = GetBoolSetting(configuration, "enableTeamsChannel", false);

                        if (!enableTeamsChannel)
                        {
                            return new DisabledTeamsChannelRepository();
                        }

                        var telemetryClient = services.GetRequiredService<TelemetryClient>();

                        var teamsGraphCredentials = services.GetService<IOptionsSnapshot<GraphCredentials>>().Get("TeamsGraphCredentials");

                        var channelReadWriteApplicationPermissionGranted = GetBoolSetting(configuration, "TeamsChannel:IsChannelReadWriteApplicationPermissionGranted", false);

                        TokenCredential graphTokenCredential;

                        if (channelReadWriteApplicationPermissionGranted)
                        {
                            graphTokenCredential = FunctionAppDI.CreateAuthProviderFromSecret(teamsGraphCredentials);
                        }
                        else
                        {
                            teamsGraphCredentials.ServiceAccountUserName = configuration["teamsChannelServiceAccountUsername"];
                            teamsGraphCredentials.ServiceAccountPassword = configuration["teamsChannelServiceAccountPassword"];

                            graphTokenCredential = FunctionAppDI.CreateServiceAccountAuthProvider(teamsGraphCredentials);
                        }
                        var graphServiceClient = new GraphServiceClient(graphTokenCredential);

                        var teamsChannelRepositoryLogger = services.GetRequiredService<ILogger<TeamsChannelRepository>>();
                        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

                        return new TeamsChannelRepository(graphServiceClient, telemetryClient, teamsChannelRepositoryLogger, loggerFactory);
                    });

                    services.AddSingleton<IServiceBusTopicsRepository>(services =>
                    {
                        var serviceBusSyncJobTopic = GetValueOrThrow(configuration, "serviceBusSyncJobTopic");
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(serviceBusSyncJobTopic);
                        return new ServiceBusTopicsRepository(sender);
                    });

                    services.AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(notificationsQueue);
                        return new ServiceBusQueueRepository(sender);
                    });

                    services.AddScoped<IJobTriggerService, JobTriggerService>();
                    services.AddScoped<ISyncJobStatusService, SyncJobStatusService>();

                    var jsonSchemasPath = Path.Combine(rootPath, "JsonSchemas");
                    var schemaProvider = new JsonSchemaProvider();
                    if (Directory.Exists(jsonSchemasPath))
                    {
                        var files = Directory.EnumerateFiles(jsonSchemasPath);
                        foreach (var file in files)
                        {
                            schemaProvider.Schemas.Add(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
                        }
                    }

                    services.AddSingleton(schemaProvider);
                }).Build();

            host.Run();
        }

        private static bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            return checkParse ? value : defaultValue;
        }

        private static int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var checkParse = int.TryParse(configuration[settingName], out int value);
            return checkParse ? value : defaultValue;
        }

        private static string GetValueOrThrow(IConfiguration configuration, string key)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            throw new ArgumentNullException($"Could not start because of missing configuration option: {key}.");
        }
    }
}
