// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Entities;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.MembershipDifference;
using Repositories.SyncJobsRepository;
using System;
using Azure.Identity;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.GraphUpdater.Startup))]

namespace Hosts.GraphUpdater
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(GraphUpdater);

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddAzureAppConfiguration(options =>
            {
                options.Connect(new Uri(GetValueOrThrow("appConfigurationEndpoint")), new DefaultAzureCredential());
            });
            var configurationRoot = configBuilder.Build();

            builder.Services.AddOptions<SyncJobRepoCredentials<SyncJobRepository>>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ConnectionString = configuration.GetValue<string>("jobsStorageAccountConnectionString");
                settings.TableName = configuration.GetValue<string>("jobsTableName");
                settings.GraphUpdaterDryRun = bool.TryParse(configurationRoot["GraphUpdater:IsGraphUpdaterDryRunEnabled"], out bool value);
            });

            builder.Services.AddSingleton<IMembershipDifferenceCalculator<AzureADUser>, MembershipDifferenceCalculator<AzureADUser>>()
            .AddSingleton<IGraphServiceClient>((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddSingleton<IGraphGroupRepository, GraphGroupRepository>()
            .AddSingleton<ISyncJobRepository>(services =>
            {
                var creds = services.GetService<IOptions<SyncJobRepoCredentials<SyncJobRepository>>>();
                return new SyncJobRepository(creds.Value.ConnectionString, creds.Value.TableName, services.GetService<ILoggingRepository>());
            })
            .AddSingleton<SessionMessageCollector>()
            .AddSingleton<IGraphUpdater, GraphUpdaterApplication>()
			.AddSingleton<IDryRunValue>(services =>
			{
				return new DryRunValue(services.GetService<IOptions<DryRunValue>>().Value.DryRunEnabled);
			});

			builder.Services.AddOptions<DryRunValue>().Configure<IConfiguration>((settings, configuration) =>
			{
                settings.DryRunEnabled = bool.TryParse(configurationRoot["GraphUpdater:IsGraphUpdaterDryRunEnabled"], out bool value);
            });
        }
    }
}
