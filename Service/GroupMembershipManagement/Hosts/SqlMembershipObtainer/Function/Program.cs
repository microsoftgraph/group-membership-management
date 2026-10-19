// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Microsoft.EntityFrameworkCore;
using Repositories.EntityFramework;
using Repositories.EntityFramework.AdfContexts;
using Repositories.ServiceBusQueue;
using Repositories.SqlMembershipRepository;
using Models;
using Services;
using Services.Contracts;
using BusinessLogic.SyncJobUpdater;
using System;
using System.IO;

namespace Hosts.SqlMembershipObtainer
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
                    var functionName = "SqlMembershipObtainer";
                    var dryRunSettingName = "SqlMembershipObtainer:IsDryRunEnabled";
                    var rootPath = context.HostingEnvironment.ContentRootPath;

                    CommonServices.ConfigureCommonServices(services, configuration, functionName, dryRunSettingName, rootPath);
                    services.ConfigureFunctionsApplicationInsights();

                    services.AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
                    {
                        var storageAccountName = configuration["membershipStorageAccountName"];
                        var containerName = configuration["membershipContainerName"];

                        return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
                    });

                    services.AddSingleton<IKeyVaultSecret<ISqlMembershipRepository>>(services =>
                        new KeyVaultSecret<ISqlMembershipRepository>(CommonServices.GetValueOrThrowBase(configuration, "sqlServerMSIConnectionString")));
                    services.AddSingleton<ISqlMembershipRepository, SqlMembershipRepository>();

                    services.AddDbContext<AdfContext>(options =>
                        options.UseSqlServer(CommonServices.GetValueOrThrowBase(configuration, "sqlServerMSIConnectionString"), sqlServerOptions =>
                        {
                            sqlServerOptions.EnableRetryOnFailure();
                        }));
                    services.AddScoped<IAdfRunHistoryRepository, AdfRunHistoryRepository>();

                    services.AddGraphAPIClient();

                    services.AddScoped<ISyncJobStatusService, SyncJobStatusService>();
                    services.AddScoped<ISqlMembershipObtainerService, SqlMembershipObtainerService>();

                    services.AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
                    {
                        var membershipAggregatorQueue = configuration["serviceBusMembershipAggregatorQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(membershipAggregatorQueue);
                        return new ServiceBusQueueRepository(sender);
                    });

                    // The notifications queue repository is built inline (not resolved from DI) so it targets the
                    // notifications queue rather than the membership-aggregator queue registered above as the default
                    // IServiceBusQueueRepository.
                    services.AddScoped<INotificationSenderService, NotificationSenderService>(services =>
                    {
                        var notificationsQueue = configuration["serviceBusNotificationsQueue"];
                        var client = services.GetRequiredService<ServiceBusClient>();
                        var sender = client.CreateSender(notificationsQueue);
                        var notificationsQueueRepository = new ServiceBusQueueRepository(sender);

                        return new NotificationSenderService(
                            notificationsQueueRepository,
                            services.GetRequiredService<ILogger<NotificationSenderService>>());
                    });
                })
                .Build();

            host.Run();
        }
    }
}
