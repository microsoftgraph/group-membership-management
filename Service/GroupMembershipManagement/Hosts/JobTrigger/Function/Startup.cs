// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
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
using Repositories.ServiceBusTopics;
using Repositories.TeamsChannel;
using Services;
using Services.Contracts;
using System;
using System.IO;

[assembly: FunctionsStartup(typeof(Hosts.JobTrigger.Startup))]

namespace Hosts.JobTrigger
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(JobTrigger);
        protected override string DryRunSettingName => string.Empty;

        private const string SCHEMA_DIRECTORY = "JsonSchemas";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddOptions<JobTriggerConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.GMMHasGroupReadWriteAllPermissions = GetBoolSetting(configuration, "JobTrigger:IsGroupReadWriteAllGranted", false);
                settings.JobCountThreshold = GetIntSetting(configuration, "JobTrigger:JobCountThreshold", 100); 
                settings.JobPercentThreshold = GetIntSetting(configuration, "JobTrigger:JobPercentThreshold", 25);
            });

            builder.Services.AddSingleton<IJobTriggerConfig>(services => services.GetService<IOptions<JobTriggerConfig>>().Value);

            builder.Services.AddSingleton<IKeyVaultSecret<IJobTriggerService>>(services => new KeyVaultSecret<IJobTriggerService>(services.GetService<IOptions<GraphCredentials>>().Value.ClientId))
            .AddSingleton<IKeyVaultSecret<IJobTriggerService, Guid>>(services =>
            {
                var configuration = services.GetService<IConfiguration>();
                return new KeyVaultSecret<IJobTriggerService, Guid>(Guid.Parse(configuration["teamsChannelServiceAccountObjectId"]));
            });

            builder.Services.AddSingleton((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

            builder.Services.AddTransient<ITeamsChannelRepository, TeamsChannelRepository>((services) =>
            {
                var loggingRepository = services.GetRequiredService<ILoggingRepository>();
                var telemetryClient = services.GetRequiredService<TelemetryClient>();

                var configuration = services.GetService<IConfiguration>();
                var graphCredentials = services.GetService<IOptions<GraphCredentials>>().Value;
                graphCredentials.ServiceAccountUserName = configuration["teamsChannelServiceAccountUsername"];
                graphCredentials.ServiceAccountPassword = configuration["teamsChannelServiceAccountPassword"];
                var graphServiceClient = new GraphServiceClient(FunctionAppDI.CreateServiceAccountAuthProvider(graphCredentials));

                return new TeamsChannelRepository(loggingRepository, graphServiceClient, telemetryClient);
            });

            builder.Services.AddSingleton<IServiceBusTopicsRepository>(services =>
            {
                var serviceBusSyncJobTopic = GetValueOrThrow("serviceBusSyncJobTopic");
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(serviceBusSyncJobTopic);
                return new ServiceBusTopicsRepository(sender);
            });

            builder.Services.AddScoped<IJobTriggerService, JobTriggerService>();

            var rootPath = builder.GetContext().ApplicationRootPath;
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

            builder.Services.AddSingleton(schemaProvider);
        }

        private bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            return checkParse ? value : defaultValue;
        }
        private int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var checkParse = int.TryParse(configuration[settingName], out int value);
            return checkParse ? value : defaultValue;
        }
    }
}
