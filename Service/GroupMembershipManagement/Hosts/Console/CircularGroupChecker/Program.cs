using Entities;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.SecurityGroupCrawler;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace CircularGroupChecker
{
	class Program
	{
		// using Powershell's client ID for now.
		const string ClientId = "1950a258-227b-4e31-a9cf-717495945fc2";
		const string Tenant = "72f988bf-86f1-41af-91ab-2d7cd011db47";

		static async Task Main(string[] args)
		{
			var publicClientApp = PublicClientApplicationBuilder.Create(ClientId)
				.WithRedirectUri("http://localhost")
				.WithAuthority(AzureCloudInstance.AzurePublic, Tenant)
				.Build();

			var repo = new GraphGroupRepository(new InteractiveAuthenticationProvider(publicClientApp), new Logger());

			bool compareGroups = args.Length == 2;
			var users = new List<HashSet<AzureADUser>>();
			var cycles = new List<SecurityGroupCycle>();
			foreach (var groupToTraverse in args.Select(x => Guid.Parse(x)))
			{
				Console.WriteLine("Checking group: " + groupToTraverse);

				if (await repo.GroupExists(groupToTraverse) == false)
				{
					Console.WriteLine($"{groupToTraverse} doesn't exist. Skipping.");
					continue;
				}

				var crawler = new SGCrawler(repo);
				var crawlerUsers = new ConcurrentDictionary<AzureADUser, byte>();
				var crawlerGroups = new ConcurrentBag<AzureADGroup>();
				var crawlerCycles = new ConcurrentBag<SecurityGroupCycle>();

				crawler.FoundUserAction = (user) => { crawlerUsers.TryAdd(user, 0); };
				crawler.FoundGroupAction = (group) => { crawlerGroups.Add(group); };
				crawler.FoundGroupCycleAction = (cycle) => { crawlerCycles.Add(cycle); };

				var stopwatch = Stopwatch.StartNew();
				await crawler.CrawlGroup(new AzureADGroup { ObjectId = groupToTraverse });
				stopwatch.Stop();
				Console.WriteLine($"Found {crawlerUsers.Count} total ({crawlerUsers.Keys.Distinct().Count()} distinct) users in {crawlerGroups.Count} total ({crawlerGroups.Distinct().Count()} distinct) nested groups in {stopwatch.ElapsedMilliseconds} ms.");

				Console.WriteLine($"Found {crawlerCycles.Count} cycles.");
				if (crawlerCycles.Any())
				{
					cycles.AddRange(crawlerCycles);
					foreach (var cycle in crawlerCycles)
					{
						Console.WriteLine(cycle);
					}
				}


				if (compareGroups)
					users.Add(new HashSet<AzureADUser>(crawlerUsers.Keys));
			}

			if (compareGroups && users.Count == 2)
			{
				Console.WriteLine($"The following users are in {args[0]} but not {args[1]}:");
				Console.WriteLine(string.Join(Environment.NewLine, users[0].Except(users[1])));
				Console.WriteLine($"The following users are in {args[1]} but not {args[0]}:");
				Console.WriteLine(string.Join(Environment.NewLine, users[1].Except(users[0])));
			}

			if (cycles.Any())
			{
				Console.WriteLine($"Found {cycles.Count} cycles across all groups checked.");
				foreach (var cycle in cycles)
				{
					Console.WriteLine(cycle);
				}
			}
		}

		private class Logger : ILoggingRepository
		{
			public string SyncJobInfo { get; set; }

			public Task LogMessageAsync(LogMessage logMessage)
			{
				// You can put a console.writeline in here if you want to see output while it runs.
				return Task.CompletedTask;
			}
		}
	}
}
