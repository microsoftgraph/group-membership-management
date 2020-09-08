// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.SecurityGroupCrawler;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Help = Tests.Repositories.TestObjectHelpers;

namespace Tests.Repositories
{
	[TestClass]
	public class SecurityGroupCrawlerTests
	{
		private readonly Help _help = new Help();

		[TestMethod]
		public async Task FindsUsersWithoutCycles()
		{
			var collector = new CrawlResultCollector();
			var groups = MakeNoCycleDictionary();
			var crawler = collector.Attach(new SGCrawler(new MockSGRepository(groups, TimeSpan.FromSeconds(0))));

			await crawler.CrawlGroup(_help.GroupNamed(0));

			CollectionAssert.AreEquivalent(new[] { _help.UserNamed(0), _help.UserNamed(1), _help.UserNamed(2), _help.UserNamed(3) },
				collector.Users.Distinct().ToArray());
			CollectionAssert.AreEquivalent(new[] { _help.GroupNamed(0), _help.GroupNamed(1), _help.GroupNamed(2) },
				collector.Groups.ToArray()); //No distinct, should visit each group exactly once.
			Assert.AreEqual(0, collector.Cycles.Count);
		}

		[TestMethod]
		public async Task FindsUsersWithCycles()
		{
			var collector = new CrawlResultCollector();
			var groups = MakeCycleDictionary();
			var crawler = collector.Attach(new SGCrawler(new MockSGRepository(groups, TimeSpan.FromSeconds(0))));

			await crawler.CrawlGroup(_help.GroupNamed(0));

			CollectionAssert.AreEquivalent(new[] { _help.UserNamed(0), _help.UserNamed(1), _help.UserNamed(2), _help.UserNamed(3), _help.UserNamed(4) },
				collector.Users.Distinct().ToArray());
			CollectionAssert.AreEquivalent(new[] { _help.GroupNamed(0), _help.GroupNamed(1), _help.GroupNamed(2), _help.GroupNamed(3) },
				collector.Groups.ToArray());
			var foundcycle = collector.Cycles.Single();
			Assert.AreEqual(_help.GroupNamed(0), foundcycle.Group);

			// equal here because order matters
			CollectionAssert.AreEqual(new[] { _help.GroupNamed(0), _help.GroupNamed(1), _help.GroupNamed(3) }, foundcycle.Cycle);
		}

		[TestMethod]
		public async Task FindsUsersWithSizeOneCycles()
		{
			var collector = new CrawlResultCollector();
			var groups = new Dictionary<AzureADGroup, IEnumerable<IAzureADObject>>()
			{
				{ _help.GroupNamed(0), new IAzureADObject[] { _help.GroupNamed(0), _help.UserNamed(0) } }
			};
			var crawler = collector.Attach(new SGCrawler(new MockSGRepository(groups, TimeSpan.FromSeconds(0))));

			await crawler.CrawlGroup(_help.GroupNamed(0));

			Assert.AreEqual(_help.UserNamed(0), collector.Users.Single());
			Assert.AreEqual(_help.GroupNamed(0), collector.Groups.Single());

			var foundcycle = collector.Cycles.Single();
			Assert.AreEqual(_help.GroupNamed(0), foundcycle.Group);
			Assert.AreEqual(_help.GroupNamed(0), foundcycle.Cycle.Single());
		}

		[DataTestMethod]
		[DataRow(1000, 1)]
		[DataRow(1, 1000)]
		[DataRow(2500, 1000)]
		[DataRow(25000, 1000)]
		[DataRow(250000, 1000)]
		[DataRow(500000, 1000)]
		[DataRow(500000, 2000)]
		//[DataRow(500000, 2000, 60)]
		[DataRow(1000000, 2000)]
		[DataRow(1000000, 3000)]
		[DataRow(2000000, 5000)]
		//[DataRow(2000000, 5000, 60)]
		[DataRow(2000000, 10000)]
		[DataRow(2000000, 20000)]
		[DataRow(5000000, 50000)]
		[DataRow(10000000, 100000)]
		[DataRow(10000000, 1000000)]
		//[DataRow(5000000, 50000, 30)] //These do work, but they take a while to run!
		public async Task HandlesLargeTrees(int userCount, int groupCount, int delaySeconds = 0)
		{
			var counter = new CrawlResultCounter();
			var (groups, expectedCycles, head) = MakeBigDictionary(userCount, groupCount);
			var crawler = counter.Attach(new SGCrawler(new MockSGRepository(groups, TimeSpan.FromSeconds(delaySeconds))));

			await crawler.CrawlGroup(head);

			Assert.IsTrue(userCount <= counter.Users);
			Assert.AreEqual(groupCount, counter.Groups);
			Assert.IsTrue(expectedCycles <= counter.Cycles);
		}


		private class CrawlResultCollector
		{
			public ConcurrentBag<AzureADUser> Users { get; private set; } = new ConcurrentBag<AzureADUser>();
			public ConcurrentBag<AzureADGroup> Groups { get; private set; } = new ConcurrentBag<AzureADGroup>();
			public ConcurrentBag<SecurityGroupCycle> Cycles { get; private set; } = new ConcurrentBag<SecurityGroupCycle>();

			public void FoundUser(AzureADUser user) => Users.Add(user);
			public void FoundGroup(AzureADGroup group) => Groups.Add(group);
			public void FoundCycle(SecurityGroupCycle cycle) => Cycles.Add(cycle);

			public SGCrawler Attach(SGCrawler crawler)
			{
				crawler.FoundUserAction = FoundUser;
				crawler.FoundGroupAction = FoundGroup;
				crawler.FoundGroupCycleAction = FoundCycle;
				return crawler;
			}
		}

		private class CrawlResultCounter
		{
			private int _users = 0;
			private int _groups = 0;
			private int _cycles = 0;

			public int Users { get => _users; }
			public int Groups { get => _groups; }
			public int Cycles { get => _cycles; }
			public void FoundUser(AzureADUser user) => Interlocked.Increment(ref _users);
			public void FoundGroup(AzureADGroup group) => Interlocked.Increment(ref _groups);
			public void FoundCycle(SecurityGroupCycle cycle) => Interlocked.Increment(ref _cycles);

			public SGCrawler Attach(SGCrawler crawler)
			{
				crawler.FoundUserAction = FoundUser;
				crawler.FoundGroupAction = FoundGroup;
				crawler.FoundGroupCycleAction = FoundCycle;
				return crawler;
			}
		}

		private Dictionary<AzureADGroup, IEnumerable<IAzureADObject>> MakeNoCycleDictionary()
		{
			return new Dictionary<AzureADGroup, IEnumerable<IAzureADObject>>
			{
				{ _help.GroupNamed(0), new IAzureADObject[] { _help.GroupNamed(1), _help.UserNamed(0) }  },
				{ _help.GroupNamed(1), new IAzureADObject[] { _help.GroupNamed(2), _help.UserNamed(0), _help.UserNamed(1), _help.UserNamed(2) }  },
				{ _help.GroupNamed(2), new IAzureADObject[] { _help.UserNamed(3) }  },
				{ _help.GroupNamed(3), new IAzureADObject[] { _help.UserNamed(3) }  },
			};
		}

		private Dictionary<AzureADGroup, IEnumerable<IAzureADObject>> MakeCycleDictionary()
		{
			return new Dictionary<AzureADGroup, IEnumerable<IAzureADObject>>
			{
				{ _help.GroupNamed(0), new IAzureADObject[] { _help.GroupNamed(1), _help.UserNamed(0) }  },
				{ _help.GroupNamed(1), new IAzureADObject[] { _help.GroupNamed(2), _help.GroupNamed(3), _help.UserNamed(0), _help.UserNamed(1), _help.UserNamed(2) }  },
				{ _help.GroupNamed(2), new IAzureADObject[] { _help.UserNamed(3) }  },
				{ _help.GroupNamed(3), new IAzureADObject[] { _help.UserNamed(4), _help.GroupNamed(0) }  },
			};
		}

		private (Dictionary<AzureADGroup, IEnumerable<IAzureADObject>> Groups, int Cycles, AzureADGroup head) MakeBigDictionary(int users, int groups)
		{
			var mockUsers = Enumerable.Range(0, users).Select(x => _help.UserNamed(x)).ToArray();
			var mockGroups = Enumerable.Range(0, groups).Select(x => _help.GroupNamed(x)).ToArray();

			var toreturn = new Dictionary<AzureADGroup, IEnumerable<IAzureADObject>>();

			foreach (var group in mockGroups)
			{
				List<IAzureADObject> toAdd = new List<IAzureADObject>();
				for (int usersToAdd = 0; usersToAdd < Help.Rand.Next(0, 100); usersToAdd++)
				{
					toAdd.Add(Help.RandomSample(mockUsers));
				}

				for (int groupsToAdd = 0; groupsToAdd < Help.Rand.Next(0, 50); groupsToAdd++)
				{
					toAdd.Add(Help.RandomSample(mockGroups));
				}

				toreturn.Add(group, toAdd);
			}

			// Ensure there's a few cycles
			int expectedCycles = Help.Rand.Next(2, 5);
			for (int cyclesToAdd = 0; cyclesToAdd < expectedCycles; cyclesToAdd++)
			{
				var headGroup = Help.RandomSample(mockGroups);
				var prevGroup = headGroup;
				var tailGroup = headGroup;
				for (int cycleLength = 0; cycleLength < Help.Rand.Next(0, 5); cycleLength++)
				{
					tailGroup = Help.RandomSample(mockGroups);
					(toreturn[prevGroup] as List<IAzureADObject>).Add(tailGroup);
					prevGroup = tailGroup;
				}
				(toreturn[headGroup] as List<IAzureADObject>).Add(tailGroup);
			}

			// ensure each user is in at least one group
			foreach (var user in mockUsers)
			{
				(toreturn[Help.RandomSample(mockGroups)] as List<IAzureADObject>).Add(user);
			}

			// ensure there's one head group with all the others as children
			var head = Help.RandomSample(mockGroups);
			(toreturn[head] as List<IAzureADObject>).AddRange(mockGroups);

			return (toreturn, expectedCycles, head);
		}

		

	}
}

