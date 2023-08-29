// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Hosts.FunctionBase;
using Services;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.ServiceBusTopics;
using Services.Contracts;
using Common.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.GraphGroups;
using Microsoft.Graph;
using Microsoft.Extensions.Configuration;
using Azure.Messaging.ServiceBus;
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
            .AddSingleton((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>();

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
