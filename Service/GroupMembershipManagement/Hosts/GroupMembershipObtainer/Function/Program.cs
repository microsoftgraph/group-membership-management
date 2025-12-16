// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.


using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using System;
using BusinessLogic.SyncJobUpdater;
using Services.Contracts;

namespace Hosts.GroupMembershipObtainer
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
                        options.Connect(new Uri(appConfigEndpoint), credential)
                            .UseFeatureFlags();
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;
                    var functionName = "GroupMembershipObtainer";
                    var dryRunSettingName = "GroupMembershipObtainer:IsDryRunEnabled";
                    var rootPath = context.HostingEnvironment.ContentRootPath;

                    // Enable automatic HttpClient dependency tracking (configurable)
                    services.AddSingleton<ITelemetryModule>(sp =>
                    {
                        var cfg = sp.GetRequiredService<IConfiguration>();
                        var enableHttpListener = CommonServices.GetBoolSettingBase(cfg, "GroupMembershipObtainer:EnableHttpHandlerDiagnosticListener", false);
                        var module = new DependencyTrackingTelemetryModule();
                        if (enableHttpListener)
                        {
                            module.IncludeDiagnosticSourceActivities.Add("HttpHandlerDiagnosticListener");
                        }
                        return module;
                    });

                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
                    services.AddOptions<DeltaCachingConfig>().Configure<IConfiguration>((settings, configuration) =>
                    {
                        settings.DeltaCacheEnabled = CommonServices.GetBoolSettingBase(configuration, "GroupMembershipObtainer:IsDeltaCacheEnabled", false);
                    });
                    services.AddSingleton<IDeltaCachingConfig>(services =>
                    {
                        return new DeltaCachingConfig(services.GetService<IOptions<DeltaCachingConfig>>().Value.DeltaCacheEnabled);
                    });
                    services.AddScoped<ISyncJobStatusService, SyncJobStatusService>();
                    services.AddGraphAPIClient()
                    .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
                    .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                    {
                        var configuration = s.GetService<IConfiguration>();
                        var storageAccountName = configuration["membershipStorageAccountName"];
                        var containerName = configuration["membershipContainerName"];

                        return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                    })
                    .AddScoped<SGMembershipCalculator>(services =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(notificationsQueue);
                        var notificationsQueueRepository = new ServiceBusQueueRepository(sender);

                        return new SGMembershipCalculator(
                            services.GetRequiredService<IGraphGroupRepository>(),
                            services.GetRequiredService<IBlobStorageRepository>(),
                            services.GetRequiredService<IDatabaseSyncJobsRepository>(),
                            services.GetRequiredService<IDatabaseGroupsRepository>(),
                            services.GetRequiredService<IDatabaseChannelsRepository>(),
                            notificationsQueueRepository,
                            services.GetRequiredService<IDatabaseDestinationAttributesRepository>(),
                            services.GetRequiredService<ILoggingRepository>(),
                            services.GetRequiredService<IDryRunValue>(),
                            services.GetRequiredService<ISyncJobStatusService>()
                        );
                    })
                    .AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                    {
                        var configuration = services.GetRequiredService<IConfiguration>();
                        var membershipAggregatorQueue = configuration["serviceBusMembershipAggregatorQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(membershipAggregatorQueue);
                        return new ServiceBusQueueRepository(sender);
                    });
                })
                .Build();
            host.Run();
        }
    }
}
