// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Common.DependencyInjection;
using Entities;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.MembershipDifference;
using Repositories.SyncJobsRepository;
using System;
using System.Collections.Generic;
using System.Text;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.GraphUpdater.Startup))]

namespace Hosts.GraphUpdater
{
	public class Startup : FunctionsStartup
	{
		public override void Configure(IFunctionsHostBuilder builder)
		{
			builder.Services.AddOptions<GraphCredentials>().Configure<IConfiguration>((settings, configuration) =>
			{
				configuration.GetSection("graphCredentials").Bind(settings);
			});

			builder.Services.AddOptions<SyncJobRepoCredentials>().Configure<IConfiguration>((settings, configuration) =>
			{
				settings.ConnectionString = configuration.GetValue<string>("jobsStorageAccountConnectionString");
				settings.TableName = configuration.GetValue<string>("jobsTableName");
			});

			builder.Services.AddSingleton<IMembershipDifferenceCalculator<AzureADUser>, MembershipDifferenceCalculator<AzureADUser>>()
			.AddSingleton((services) =>
			{
				return FunctionAppDI.CreateAuthProvider(services.GetService<IOptions<GraphCredentials>>().Value);
			})
			.AddSingleton<IGraphGroupRepository, GraphGroupRepository>()
            .AddSingleton<ISyncJobRepository>(services =>
			{
				var creds = services.GetService<IOptions<SyncJobRepoCredentials>>();
				return new SyncJobRepository(creds.Value.ConnectionString, creds.Value.TableName);
			})
			.AddSingleton<SessionMessageCollector>()
			.AddSingleton<IGraphUpdater, GraphUpdaterApplication>();
		}

		private class SyncJobRepoCredentials
		{
			public string ConnectionString { get; set; }
			public string TableName { get; set; }
		}
	}

}

