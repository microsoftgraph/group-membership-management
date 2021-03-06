// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using Repositories.SyncJobsRepository;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.SecurityGroup.Startup))]

namespace Hosts.SecurityGroup
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(SecurityGroup);

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddOptions<ServiceBusConfiguration>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.Namespace = configuration.GetValue<string>("differenceServiceBusNamespace");
                settings.QueueName = configuration.GetValue<string>("membershipQueueName");
            });

            builder.Services.AddOptions<SyncJobRepoCredentials<SyncJobRepository>>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ConnectionString = configuration.GetValue<string>("jobsStorageAccountConnectionString");
                settings.TableName = configuration.GetValue<string>("jobsTableName");
            });

            builder.Services.AddSingleton<IGraphServiceClient>((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddSingleton<IMembershipServiceBusRepository, MembershipServiceBusRepository>((services) =>
            {
                var config = services.GetService<IOptions<ServiceBusConfiguration>>().Value;
                return new MembershipServiceBusRepository(serviceBusNamespacePrefix: config.Namespace, queueName: config.QueueName);
            })
            .AddSingleton<ISyncJobRepository>(services =>
            {
                var creds = services.GetService<IOptions<SyncJobRepoCredentials<SyncJobRepository>>>();
                return new SyncJobRepository(creds.Value.ConnectionString, creds.Value.TableName, services.GetService<ILoggingRepository>());
            })
            .AddSingleton<IGraphGroupRepository, GraphGroupRepository>()
            .AddSingleton<SGMembershipCalculator>();
        }

        private class ServiceBusConfiguration
        {
            public string Namespace { get; set; }
            public string QueueName { get; set; }
        }
    }
}
