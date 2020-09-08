// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Common.DependencyInjection;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.ServiceBusQueue;
using System;
using System.Collections.Generic;
using System.Text;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.SecurityGroupMembershipCalculator.Startup))]

namespace Hosts.SecurityGroupMembershipCalculator
{
	public class Startup : FunctionsStartup
	{
		public override void Configure(IFunctionsHostBuilder builder)
		{
			builder.Services.AddOptions<GraphCredentials>().Configure<IConfiguration>((settings, configuration) =>
			{
				configuration.GetSection("graphCredentials").Bind(settings);
			});

			builder.Services.AddOptions<ServiceBusConfiguration>().Configure<IConfiguration>((settings, configuration) =>
			{
				settings.Namespace = configuration.GetValue<string>("differenceServiceBusNamespace");
				settings.QueueName = configuration.GetValue<string>("membershipQueueName");
			});

			builder.Services.AddScoped((services) =>
			 {
				 return FunctionAppDI.CreateAuthProvider(services.GetService<IOptions<GraphCredentials>>().Value);
			 })
			.AddScoped<IMembershipServiceBusRepository, MembershipServiceBusRepository>((services) =>
			{
				var config = services.GetService<IOptions<ServiceBusConfiguration>>().Value;
				return new MembershipServiceBusRepository(serviceBusNamespacePrefix: config.Namespace, queueName: config.QueueName);
			})
			.AddScoped<IGraphGroupRepository, GraphGroupRepository>()
			.AddScoped<SGMembershipCalculator>();
		}

		private class ServiceBusConfiguration
		{
			public string Namespace { get; set; }
			public string QueueName { get; set; }
		}
	}
}

