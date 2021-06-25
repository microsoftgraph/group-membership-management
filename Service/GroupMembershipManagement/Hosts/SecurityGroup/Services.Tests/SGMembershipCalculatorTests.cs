// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Hosts.SecurityGroup;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mocks;
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
			var allUsers = new List<AzureADUser> { };
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
			var mail = new MockMailRepository();
			var mailAddresses = new MockEmail<IEmailSenderRecipient>();
			var syncJobs = new MockSyncJobRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, mail, mailAddresses, syncJobs, new MockLoggingRepository());

			var testJob = new SyncJob
			{
				RowKey = "row",
				PartitionKey = "partition",
				TargetOfficeGroupId = destinationGroup,
				Query = sourceGroup.ToString(),
				Status = "InProgress"
			};

			syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

			var groups = calc.ReadSourceGroups(testJob);
			await calc.SendMembershipAsync(testJob, Guid.NewGuid(), allUsers);
			foreach (var group in groups)
			{
				var groupExistsResult = await calc.GroupExistsAsync(group.ObjectId, Guid.NewGuid());
				Assert.AreEqual(OutcomeType.Successful, groupExistsResult.Outcome);
				Assert.AreEqual(true, groupExistsResult.Result);
			}
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
			var allUsers = new List<AzureADUser> {};

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
			var mail = new MockMailRepository();
			var mailAddresses = new MockEmail<IEmailSenderRecipient>();
			var syncJobs = new MockSyncJobRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, mail, mailAddresses, syncJobs, new MockLoggingRepository());

			var testJob = new SyncJob
			{
				RowKey = "row",
				PartitionKey = "partition",
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups),
				Status = "InProgress"
			};

			syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

			var groups = calc.ReadSourceGroups(testJob);
			await calc.SendMembershipAsync(testJob, Guid.NewGuid(), allUsers);

			foreach (var group in groups)
			{
				var groupExistsResult = await calc.GroupExistsAsync(group.ObjectId, Guid.NewGuid());
				Assert.AreEqual(OutcomeType.Successful, groupExistsResult.Outcome);
				Assert.AreEqual(true, groupExistsResult.Result);
			}
		}

		[TestMethod]
		[DataRow(0, 0)]
		[DataRow(3, 0)]
		[DataRow(0, 3)]
		[DataRow(3, 3)]
		public async Task ProperlyGetsMembersFromPages(int getGroupExceptions, int getMembersExceptions)
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
            var mail = new MockMailRepository();
            var mailAddresses = new MockEmail<IEmailSenderRecipient>();
            var syncJobs = new MockSyncJobRepository();

            var calc = new SGMembershipCalculator(graphRepo, serviceBus, mail, mailAddresses, syncJobs, new MockLoggingRepository());

            var testJob = new SyncJob
            {
                RowKey = "row",
                PartitionKey = "partition",
                TargetOfficeGroupId = destinationGroup,
                Query = string.Join(';', sourceGroups),
                Status = "InProgress"
            };

            syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);
            var groups = calc.ReadSourceGroups(testJob);
			foreach (var group in groups)
			{
				var groupExistsResult = await calc.GroupExistsAsync(group.ObjectId, Guid.NewGuid());
				var response = await calc.GetFirstUsersPageAsync(group.ObjectId, Guid.NewGuid());
				Assert.IsNotNull(response.nextPageUrl);
				response = await calc.GetNextUsersPageAsync("nextPageLink", response.usersFromGroup);
				Assert.AreEqual("", response.nextPageUrl);
				Assert.AreEqual(OutcomeType.Successful, groupExistsResult.Outcome);
				Assert.AreEqual(true, groupExistsResult.Result);
			}
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
			var mail = new MockMailRepository();
			var mailAddresses = new MockEmail<IEmailSenderRecipient>();
			var syncJobs = new MockSyncJobRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, mail, mailAddresses, syncJobs, new MockLoggingRepository());

			var testJob = new SyncJob
			{
				RowKey = "row",
				PartitionKey = "partition",
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups),
				Status = "InProgress"
			};

			syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);
			var groups = calc.ReadSourceGroups(testJob);
			foreach (var group in groups)
			{
				await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Content", null);
			}

			Assert.IsNull(serviceBus.Sent);
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
			var mail = new MockMailRepository();
			var mailAddresses = new MockEmail<IEmailSenderRecipient>();
			var syncJobs = new MockSyncJobRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, mail, mailAddresses, syncJobs, new MockLoggingRepository());

			var testJob = new SyncJob
			{
				RowKey = "row",
				PartitionKey = "partition",
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups),
				Status = "InProgress"
			};
			syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);
			var groups = calc.ReadSourceGroups(testJob);
			foreach (var group in groups)
			{
				await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Content", null);
			}
			Assert.IsNull(serviceBus.Sent);
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
			var mail = new MockMailRepository();
			var mailAddresses = new MockEmail<IEmailSenderRecipient>();
			var syncJobs = new MockSyncJobRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, mail, mailAddresses, syncJobs, new MockLoggingRepository());

			Guid nonexistentGroupId = Guid.NewGuid();
			var testJob = new SyncJob
			{
				RowKey = "row",
				PartitionKey = "partition",
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups) + $";{nonexistentGroupId}",
				Status = "InProgress"
			};

			syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

			var groups = calc.ReadSourceGroups(testJob);
			foreach (var group in groups)
			{
				await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Content", null);
			}
			Assert.IsNull(serviceBus.Sent);
		}

		[TestMethod]
		[DataRow(0, 0)]
		[DataRow(3, 0)]
		[DataRow(0, 3)]
		[DataRow(3, 3)]
		public async Task ProperlyErrorsOnAllNonGuidGroups(int getGroupExceptions, int getMembersExceptions)
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
			var mail = new MockMailRepository();
			var mailAddresses = new MockEmail<IEmailSenderRecipient>();
			var syncJobs = new MockSyncJobRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, mail, mailAddresses, syncJobs, new MockLoggingRepository());

			var testJob = new SyncJob
			{
				RowKey = "row",
				PartitionKey = "partition",
				TargetOfficeGroupId = destinationGroup,
				Query = "asfdasdf;asfdoiu;oasfdjlsadhfa;;;",
				Status = "InProgress"
			};

			syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

            var groups = calc.ReadSourceGroups(testJob);
            foreach (var group in groups)
            {
                var groupExistsResult = await calc.GroupExistsAsync(group.ObjectId, Guid.NewGuid());
                Assert.AreEqual(false, groupExistsResult.Result);
            }
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
			var mail = new MockMailRepository();
			var mailAddresses = new MockEmail<IEmailSenderRecipient>();
			var syncJobs = new MockSyncJobRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, mail, mailAddresses, syncJobs, new MockLoggingRepository());

			var testJob = new SyncJob
			{
				RowKey = "row",
				PartitionKey = "partition",
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups) + $";{Guid.NewGuid()}",
				Status = "InProgress"
			};

			syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);

			var groups = calc.ReadSourceGroups(testJob);
			foreach (var group in groups)
			{
				var groupExistsResult = await calc.GroupExistsAsync(group.ObjectId, Guid.NewGuid());
				Assert.AreEqual(false, groupExistsResult.Result);
			}
			Assert.IsNull(serviceBus.Sent);
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
			var mail = new MockMailRepository();
			var mailAddresses = new MockEmail<IEmailSenderRecipient>();
			var syncJobs = new MockSyncJobRepository();

			var calc = new SGMembershipCalculator(graphRepo, serviceBus, mail, mailAddresses, syncJobs, new MockLoggingRepository());

			var testJob = new SyncJob
			{
				RowKey = "row",
				PartitionKey = "partition",
				TargetOfficeGroupId = destinationGroup,
				Query = string.Join(';', sourceGroups) + ";nasdfasfd;;;",
				Status = "InProgress"
			};

			syncJobs.ExistingSyncJobs.Add((testJob.RowKey, testJob.PartitionKey), testJob);
			var groups = calc.ReadSourceGroups(testJob);
			foreach (var group in groups)
			{
				await calc.SendEmailAsync(testJob, Guid.NewGuid(), "Content", null);
			}
			Assert.IsNull(serviceBus.Sent);
		}
	}
}