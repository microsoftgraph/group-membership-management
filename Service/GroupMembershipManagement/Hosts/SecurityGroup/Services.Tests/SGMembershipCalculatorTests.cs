// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Hosts.SecurityGroup;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
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
		[DataRow(0, 0)]
		[DataRow(3, 0)]
		[DataRow(0, 3)]
		[DataRow(3, 3)]
		public async Task ProperlyGetsAndSendsMembership(int getGroupExceptions, int getMembersExceptions)
		{
			const int userCount = 2500213;
			Guid sourceGroup = Guid.NewGuid();
			Guid destinationGroup = Guid.NewGuid();
			var initialUsers = Enumerable.Range(0, userCount).Select(
					x => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();

			var graphRepo = new MockGraphGroupRepository()
			{
				GroupsToUsers = new Dictionary<Guid, List<AzureADUser>> { { sourceGroup, initialUsers } },
				ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
				ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
			};
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, new MockLogger());

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
		[DataRow(0, 0)]
		[DataRow(3, 0)]
		[DataRow(0, 3)]
		[DataRow(3, 3)]
		public async Task ProperlyGetsAndSendsMembershipWithMultipleSources(int getGroupExceptions, int getMembersExceptions)
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

			var graphRepo = new MockGraphGroupRepository()
			{
				GroupsToUsers = mockGroups,
				ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
				ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
			};
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, new MockLogger());

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
		[DataRow(10, 0)]
		[DataRow(0, 10)]
		[DataRow(10, 10)]
		public async Task ProperlyErrorsAfterTooManyRetries(int getGroupExceptions, int getMembersExceptions)
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

			var graphRepo = new MockGraphGroupRepository()
			{
				GroupsToUsers = mockGroups,
				ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
				ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
			};
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, new MockLogger());

			await Assert.ThrowsExceptionAsync<SocketException>(() => calc.SendMembership(new SyncJob
			{
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups)
			}));

			Assert.IsTrue(serviceBus.Sent.Errored);
			Assert.AreEqual(0, serviceBus.Sent.SourceMembers.Count);
		}

		[TestMethod]
		[DataRow(true, false)]
		[DataRow(false, true)]
		[DataRow(true, true)]
		public async Task ProperlyErrorsOnUnexpectedException(bool errorOnGroupExists, bool errorOnGetUsers)
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

			var graphRepo = new MockGraphGroupRepository()
			{
				GroupsToUsers = mockGroups,
				ThrowNonSocketExceptionFromGetUsersInGroup = errorOnGetUsers,
				ThrowNonSocketExceptionFromGroupExists = errorOnGroupExists
			};
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, new MockLogger());

		 	await Assert.ThrowsExceptionAsync<MockException>(() => calc.SendMembership(new SyncJob
			{
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups)
			}));

			Assert.IsTrue(serviceBus.Sent.Errored);
			Assert.AreEqual(0, serviceBus.Sent.SourceMembers.Count);
		}

		[TestMethod]
		[DataRow(0, 0)]
		[DataRow(3, 0)]
		[DataRow(0, 3)]
		[DataRow(3, 3)]
		public async Task ProperlyErrorsOnNonexistentGroups(int getGroupExceptions, int getMembersExceptions)
		{
			const int userCount = 2500213;
			var sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
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

			var graphRepo = new MockGraphGroupRepository()
			{
				GroupsToUsers = mockGroups,
				ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
				ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
			};
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, new MockLogger());

			Guid nonexistentGroupId = Guid.NewGuid();
			await calc.SendMembership(new SyncJob
			{
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups) + $";{nonexistentGroupId}"
			});

			Assert.IsTrue(serviceBus.Sent.Errored);
			Assert.AreEqual(0, serviceBus.Sent.SourceMembers.Count);
		}

		[TestMethod]
		[DataRow(0, 0)]
		[DataRow(3, 0)]
		[DataRow(0, 3)]
		[DataRow(3, 3)]
		public async Task ProperlyErrorsOnAllNonexistentGroups(int getGroupExceptions, int getMembersExceptions)
		{
			Guid[] sourceGroups = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
			Guid destinationGroup = Guid.NewGuid();

			var graphRepo = new MockGraphGroupRepository()
			{
				GroupsToUsers = new Dictionary<Guid, List<AzureADUser>>(),
				ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
				ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
			};
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, new MockLogger());

			await calc.SendMembership(new SyncJob
			{
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups) + $";{Guid.NewGuid()}"
			});

			Assert.IsTrue(serviceBus.Sent.Errored);
			Assert.AreEqual(0, serviceBus.Sent.SourceMembers.Count);
		}

		[TestMethod]
		[DataRow(0, 0)]
		[DataRow(3, 0)]
		[DataRow(0, 3)]
		[DataRow(3, 3)]
		public async Task IgnoresNonGuidArguments(int getGroupExceptions, int getMembersExceptions)
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

			var graphRepo = new MockGraphGroupRepository()
			{
				GroupsToUsers = mockGroups,
				ThrowSocketExceptionsFromGroupExistsBeforeSuccess = getGroupExceptions,
				ThrowSocketExceptionsFromGetUsersInGroupBeforeSuccess = getMembersExceptions
			};
			var serviceBus = new MockMembershipServiceBusRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, new MockLogger());

			await calc.SendMembership(new SyncJob
			{
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups) + ";nasdfasfd;;;"
			});

			CollectionAssert.AreEquivalent(mockGroups.Values.SelectMany(x => x).ToArray(), serviceBus.Sent.SourceMembers);
			CollectionAssert.AreEqual(sourceGroups.Select(x => new AzureADGroup { ObjectId = x }).ToArray(), serviceBus.Sent.Sources);
			Assert.AreEqual(destinationGroup, serviceBus.Sent.Destination.ObjectId);
		}

		private class MockLogger : ILoggingRepository
		{
			public Dictionary<string, string> SyncJobProperties { get; set; }

			public Task LogMessageAsync(LogMessage logMessage)
			{
				return Task.CompletedTask;
			}
		}
	}
}
