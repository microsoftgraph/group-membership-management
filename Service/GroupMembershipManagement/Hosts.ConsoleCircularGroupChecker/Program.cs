// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using Repositories.GraphGroups;
using Repositories.SecurityGroupCrawler;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.ConsoleCircularGroupChecker
{
	class Program
	{
		// using Powershell's client ID for now.
		const string ClientId = "1950a258-227b-4e31-a9cf-717495945fc2";
		const string Tenant = "your azure ad tenant id";

		static async Task Main(string[] args)
		{
			var publicClientApp = PublicClientApplicationBuilder.Create(ClientId)
				.WithRedirectUri("http://localhost")
				.WithAuthority(AzureCloudInstance.AzurePublic, Tenant)
				.Build();

			var repo = new GraphGroupRepository(new InteractiveAuthenticationProvider(publicClientApp));

			var users = new List<HashSet<AzureADUser>>();
			foreach (var groupToTraverse in args.Select(x => Guid.Parse(x)))
			{
				Console.WriteLine("Checking group: " + groupToTraverse);

				Stopwatch stopwatch;

				var crawler = new SGCrawler(repo);
				var crawlerUsers = new ConcurrentDictionary<AzureADUser, byte>();
				var crawlerGroups = new ConcurrentBag<AzureADGroup>();
				var crawlerCycles = new ConcurrentBag<SecurityGroupCycle>();

				crawler.FoundUserAction = (user) => { crawlerUsers.TryAdd(user, 0); };
				crawler.FoundGroupAction = (group) => { crawlerGroups.Add(group); };
				crawler.FoundGroupCycleAction = (cycle) => { crawlerCycles.Add(cycle); };

				stopwatch = Stopwatch.StartNew();
				await crawler.CrawlGroup(new AzureADGroup { ObjectId = groupToTraverse });
				stopwatch.Stop();
				Console.WriteLine($"Crawler version found {crawlerUsers.Count} users in: {stopwatch.ElapsedMilliseconds} ms");

				Console.WriteLine($"Found {crawlerCycles.Count} cycles.");
				foreach (var cycle in crawlerCycles)
				{
					Console.WriteLine(cycle);
				}

				users.Add(new HashSet<AzureADUser>(crawlerUsers.Keys));
			}

			if (args.Length == 2)
			{
				Console.WriteLine($"The following users are in {args[0]} but not {args[1]}:");
				Console.WriteLine(string.Join(Environment.NewLine, users[0].Except(users[1])));
			}
		}
	}
}

