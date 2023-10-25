// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using SqlMembershipObtainer.Common.DependencyInjection;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.DataFactory;
using Repositories.ServiceBusQueue;
using Services.Contracts;
using Services;


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

            builder.Services.AddSingleton<ISqlMembershipObtainerSecret<ISqlMembershipObtainerService>>(new SqlMembershipObtainerSecret<ISqlMembershipObtainerService>(GetValueOrThrow("sqlMembershipStorageAccountName"), GetValueOrThrow("sqlMembershipStorageAccountConnectionString")));

            builder.Services.AddSingleton<ISqlMembershipObtainerServiceSecret>(services =>
            {
                return new SqlMembershipObtainerServiceSecret(bool.Parse(GetValueOrThrow("shouldStopSyncIfSourceNotPresentInGraph")), GetValueOrThrow("sqlServerBasicConnectionString"));
            });

            builder.Services.AddSingleton<IDataFactorySecret<IDataFactoryRepository>>(new DataFactorySecrets<IDataFactoryRepository>(GetValueOrThrow("pipeline"), GetValueOrThrow("tenantId"), GetValueOrThrow("dataFactoryName"), GetValueOrThrow("sqlMembershipAppId"), GetValueOrThrow("sqlMembershipAppPasswordCredentialValue"), GetValueOrThrow("subscriptionId"), GetValueOrThrow("dataResourceGroup")));
            builder.Services.AddSingleton<IGraphServiceAttemptsValue>(services =>
            {
                var parseMaxRetryAfterAttempts = int.TryParse(GetValueOrThrow("maxRetryAfterAttempts"), out int maxRetryAfterAttempts);
                var parseMaxExceptionHandlingAttempts = int.TryParse(GetValueOrThrow("maxExceptionHandlingAttempts"), out int maxExceptionHandlingAttempts);
                if (!parseMaxRetryAfterAttempts || !parseMaxExceptionHandlingAttempts)
                {
                    maxRetryAfterAttempts = 4;
                    maxExceptionHandlingAttempts = 2;
                }
                return new GraphServiceAttemptsValue(maxRetryAfterAttempts, maxExceptionHandlingAttempts);
            });

            builder.Services.AddSingleton<IDataFactoryRepository, DataFactoryRepository>();

            builder.Services.AddSingleton(services => new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value)));

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
