// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.DataFactory;
using Repositories.ServiceBusQueue;
using Repositories.SqlMembershipRepository;
using Services;
using Services.Contracts;


// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(SqlMembershipObtainer.Startup))]

namespace SqlMembershipObtainer
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(SqlMembershipObtainer);
        protected override string DryRunSettingName => "SqlMembershipObtainer:IsDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];

                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            });

            builder.Services.AddSingleton<IKeyVaultSecret<ISqlMembershipRepository>>(services => new KeyVaultSecret<ISqlMembershipRepository>(GetValueOrThrow("sqlServerMSIConnectionString")));
            builder.Services.AddSingleton<ISqlMembershipRepository, SqlMembershipRepository>();

            builder.Services.AddSingleton<IDataFactorySecret<IDataFactoryRepository>>(new DataFactorySecrets<IDataFactoryRepository>(GetValueOrThrow("pipeline"), GetValueOrThrow("dataFactoryName"), GetValueOrThrow("subscriptionId"), GetValueOrThrow("dataResourceGroup")));

            builder.Services.AddSingleton<IDataFactoryRepository, DataFactoryRepository>();

            builder.Services.AddGraphAPIClient();

            builder.Services.AddSingleton<IDataFactoryService, DataFactoryService>();
            builder.Services.AddScoped<ISqlMembershipObtainerService, SqlMembershipObtainerService>();

            builder.Services.AddSingleton<IServiceBusQueueRepository, ServiceBusQueueRepository>(services =>
            {
                var configuration = services.GetRequiredService<IConfiguration>();
                var membershipAggregatorQueue = configuration["serviceBusMembershipAggregatorQueue"];
                var client = services.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(membershipAggregatorQueue);
                return new ServiceBusQueueRepository(sender);
            });
        }
    }
}
