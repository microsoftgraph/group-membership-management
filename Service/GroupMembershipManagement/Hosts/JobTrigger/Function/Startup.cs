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
using Repositories.SyncJobsRepository;
using Services.Contracts;
using Common.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.GraphGroups;
using Microsoft.Graph;

[assembly: FunctionsStartup(typeof(Hosts.JobTrigger.Startup))]

namespace Hosts.JobTrigger
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(JobTrigger);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton<IKeyVaultSecret<IJobTriggerService>>(services => new KeyVaultSecret<IJobTriggerService>(services.GetService<IOptions<GraphCredentials>>().Value.ClientId))
           .AddSingleton<IGraphServiceClient>((services) =>
           {
               return new GraphServiceClient(FunctionAppDI.CreateAuthProvider(services.GetService<IOptions<GraphCredentials>>().Value));
           })
            .AddSingleton<IGraphGroupRepository, GraphGroupRepository>();

            builder.Services.AddSingleton<ISyncJobRepository>(services =>
            {
                var creds = services.GetService<IOptions<SyncJobRepoCredentials<SyncJobRepository>>>();
                return new SyncJobRepository(creds.Value.ConnectionString, creds.Value.TableName, services.GetService<ILoggingRepository>());
            });

            builder.Services.AddSingleton<IServiceBusTopicsRepository>(new ServiceBusTopicsRepository(GetValueOrThrow("serviceBusConnectionString"), GetValueOrThrow("serviceBusSyncJobTopic")));
            builder.Services.AddSingleton<IJobTriggerService, JobTriggerService>();
        }
    }
}
