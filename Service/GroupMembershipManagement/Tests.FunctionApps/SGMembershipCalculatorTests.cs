// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Entities.ServiceBus;
using Hosts.SecurityGroupMembershipCalculator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Tests.FunctionApps.Mocks;

namespace Tests.FunctionApps
{
	[TestClass]
	public class SGMembershipCalculatorTests
	{
		[TestMethod]
		public async Task ProperlyGetsAndSendsMembership()
		{
			const int userCount = 2500213;
			Guid sourceGroup = Guid.NewGuid();
			Guid destinationGroup = Guid.NewGuid();
			var initialUsers = Enumerable.Range(0, userCount).Select(
					x => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();

			var graphRepo = new MockGraphGroupRepository() { GroupsToUsers = new Dictionary<Guid, List<AzureADUser>> { { sourceGroup, initialUsers } } };
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus);

			await calc.SendMembership(new SyncJob
			{
				TargetOfficeGroupId = destinationGroup,
				Query = sourceGroup.ToString()
			});

			CollectionAssert.AreEqual(initialUsers, serviceBus.Sent.SourceMembers);
			Assert.AreEqual(sourceGroup, serviceBus.Sent.Sources.Single().ObjectId);
			Assert.AreEqual(destinationGroup, serviceBus.Sent.Destination.ObjectId);
		}

		[TestMethod]
		public async Task ProperlyGetsAndSendsMembershipWithMultipleSources()
		{
			const int userCount = 2500213;
			Guid[] sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
			Guid destinationGroup = Guid.NewGuid();

			var mockGroups = new Dictionary<Guid, List<AzureADUser>>();
			for (int i = 0; i < userCount; i++)
			{
				var currentGroup = sourceGroups[i % sourceGroups.Length];
				var userToAdd = new AzureADUser { ObjectId = Guid.NewGuid() };
				if (mockGroups.TryGetValue(currentGroup, out var users))
				{
					users.Add(userToAdd);
				}
				else
				{
					mockGroups.Add(currentGroup, new List<AzureADUser> { userToAdd });
				}
			}

			var graphRepo = new MockGraphGroupRepository() { GroupsToUsers = mockGroups };
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus);

			await calc.SendMembership(new SyncJob
			{
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups)
			});

			CollectionAssert.AreEquivalent(mockGroups.Values.SelectMany(x => x).ToArray(), serviceBus.Sent.SourceMembers);
			CollectionAssert.AreEqual(sourceGroups.Select(x => new AzureADGroup { ObjectId = x }).ToArray(), serviceBus.Sent.Sources);
			Assert.AreEqual(destinationGroup, serviceBus.Sent.Destination.ObjectId);
		}

		[TestMethod]
		public async Task IgnoresNonGuidArguments()
		{
			const int userCount = 2500213;
			Guid[] sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
			Guid destinationGroup = Guid.NewGuid();

			var mockGroups = new Dictionary<Guid, List<AzureADUser>>();
			for (int i = 0; i < userCount; i++)
			{
				var currentGroup = sourceGroups[i % sourceGroups.Length];
				var userToAdd = new AzureADUser { ObjectId = Guid.NewGuid() };
				if (mockGroups.TryGetValue(currentGroup, out var users))
				{
					users.Add(userToAdd);
				}
				else
				{
					mockGroups.Add(currentGroup, new List<AzureADUser> { userToAdd });
				}
			}

			var graphRepo = new MockGraphGroupRepository() { GroupsToUsers = mockGroups };
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus);

			await calc.SendMembership(new SyncJob
			{
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups) + ";nasdfasfd;;;"
			});

			CollectionAssert.AreEquivalent(mockGroups.Values.SelectMany(x => x).ToArray(), serviceBus.Sent.SourceMembers);
			CollectionAssert.AreEqual(sourceGroups.Select(x => new AzureADGroup { ObjectId = x }).ToArray(), serviceBus.Sent.Sources);
			Assert.AreEqual(destinationGroup, serviceBus.Sent.Destination.ObjectId);
		}
	}
}

