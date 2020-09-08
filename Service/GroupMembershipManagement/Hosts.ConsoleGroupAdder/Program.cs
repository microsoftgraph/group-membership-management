// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;
using Entities;
using Entities.ServiceBus;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Repositories.ServiceBusQueue;

namespace Hosts.ConsoleGroupAdder
{
	class Program
	{
		const string ClientId = "2ac03521-fa6c-48c4-bf03-033eb930df5e";
		const string Tenant = "03d91f7c-fb5e-466e-a6ea-392c0e965d46";
		static async Task Main(string[] args)
		{
			Guid from = Guid.Parse(args[0]);
			Guid to = Guid.Parse(args[1]);
			string serviceBusNamespace = args[2];

			Console.WriteLine($"Syncing members from group ID {from} to group ID {to}.");

			var publicClientApp = PublicClientApplicationBuilder.Create(ClientId)
				.WithRedirectUri("http://localhost")
				.WithAuthority(AzureCloudInstance.AzurePublic, Tenant)
				.Build();

			var repo = new GraphGroupRepository(new InteractiveAuthenticationProvider(publicClientApp));

			var sbqueue = new MembershipServiceBusRepository(serviceBusNamespace, "membership");

			var stopwatch = Stopwatch.StartNew();
			await sbqueue.SendMembership(new GroupMembership
			{
				Sources = new[] { new AzureADGroup { ObjectId = from } },
				Destination = new AzureADGroup { ObjectId = to },
				SourceMembers = await repo.GetUsersInGroupTransitively(from)
			});
			stopwatch.Stop();

			Console.WriteLine($"Enqueued all users in group {from} in {stopwatch.Elapsed.TotalSeconds} seconds.");
		}

		private class ServiceBusName : IKeyVaultSecret<IMembershipServiceBusRepository>
		{
			public string Secret { get; set; }
		}
	}
}

