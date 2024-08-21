// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Hosting;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Repositories.ServiceBusTopics;
using Repositories.TeamsChannel;
using Services;
using Services.Contracts;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Azure.Identity;

namespace Hosts.JobTrigger
{

    public class Program
    {
        public static void Main (string[] args)
        {
            var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureAppConfiguration((context, config) =>
            {
                var settings = config.Build();
                var appConfigEndpoint = CommonServices.GetValueOrThrowBase("appConfigurationEndpoint");

                config.AddAzureAppConfiguration(options =>
                {
                    options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential())
                           .UseFeatureFlags();
                });
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                var SCHEMA_DIRECTORY = "JsonSchemas";
                var functionName = "JobTrigger";
                var dryRunSettingName = string.Empty;
                var rootPath = context.HostingEnvironment.ContentRootPath;
                CommonServices.ConfigureCommonServices(services, configuration, "JobTrigger", dryRunSettingName, rootPath);
                services.AddOptions<JobTriggerConfig>().Configure<IConfiguration>((settings, configuration) =>
                {
                    settings.GMMHasGroupReadWriteAllPermissions = CommonServices.GetBoolSettingBase(configuration, "JobTrigger:IsGroupReadWriteAllGranted", false);
                    settings.GMMHasChannelReadWriteAllPermissions = CommonServices.GetBoolSettingBase(configuration, "TeamsChannel:IsChannelReadWriteApplicationPermissionGranted", false);
                    settings.JobCountThreshold = CommonServices.GetIntSettingBase(configuration, "JobTrigger:JobCountThreshold", 10);
                    settings.JobPerMilleThreshold = CommonServices.GetIntSettingBase(configuration, "JobTrigger:JobPerMilleThreshold", 10);
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

                services.AddTransient<ITeamsChannelRepository, TeamsChannelRepository>((services) =>
                {
                    var loggingRepository = services.GetRequiredService<ILoggingRepository>();
                    var telemetryClient = services.GetRequiredService<TelemetryClient>();

                    var configuration = services.GetService<IConfiguration>();
                    var graphCredentials = services.GetService<IOptions<GraphCredentials>>().Value;

                    var channelReadWriteApplicationPermissionGranted = CommonServices.GetBoolSettingBase(configuration, "TeamsChannel:IsChannelReadWriteApplicationPermissionGranted", false);

                    TokenCredential graphTokenCredential;

                    if (channelReadWriteApplicationPermissionGranted)
                    {
                        graphTokenCredential = FunctionAppDI.CreateAuthProviderFromSecret(graphCredentials);
                    }
                    else
                    {
                        graphCredentials.ServiceAccountUserName = configuration["teamsChannelServiceAccountUsername"];
                        graphCredentials.ServiceAccountPassword = configuration["teamsChannelServiceAccountPassword"];

                        graphTokenCredential = FunctionAppDI.CreateServiceAccountAuthProvider(graphCredentials);
                    }
                    var graphServiceClient = new GraphServiceClient(graphTokenCredential);

                    return new TeamsChannelRepository(loggingRepository, graphServiceClient, telemetryClient);
                });

                services.AddSingleton<IServiceBusTopicsRepository>(services =>
                {
                    var serviceBusSyncJobTopic = CommonServices.GetValueOrThrowBase("serviceBusSyncJobTopic");
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

                var jsonSchemasPath = Path.Combine(rootPath, SCHEMA_DIRECTORY);
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
            })
            .Build();

            host.Run();
        }
    }
}



