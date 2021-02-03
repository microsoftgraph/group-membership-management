// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GraphUpdater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Services.Tests.Mocks;
using Repositories.MembershipDifference;
using Entities;
using Repositories.Mocks;
using System.Runtime.InteropServices;

namespace Services.Tests
{
	[TestClass]
	public class GraphUpdaterTests
	{
		[TestMethod]
		public void AccumulatesMessagesAndUpdatesGraph()
		{
			var mockUpdater = new MockGraphUpdater();
			var mockLogs = new MockLoggingRepository();
			var sessionCollector = new SessionMessageCollector(mockUpdater, mockLogs);

			var mockSession = new MockMessageSession()
			{
				SessionId = "someId"
			};

			var incomingMessages = MakeMembershipMessages();

			foreach (var message in incomingMessages.SkipLast(1))
			{
				sessionCollector.HandleNewMessage(message, mockSession);

				// sessionCollector doesn't do anything until it gets the last message.
				Assert.AreEqual(0, mockUpdater.Actual.Count);
				Assert.IsFalse(mockSession.Closed);
				Assert.AreEqual(0, mockSession.CompletedLockTokens.Count);
			}

			sessionCollector.HandleNewMessage(incomingMessages.Last(), mockSession);

			Assert.IsTrue(mockSession.Closed);

			Assert.AreEqual(1, mockUpdater.Actual.Count);
			var mergedMembership = mockUpdater.Actual.Single();

			Assert.AreEqual(incomingMessages.Length, mockSession.CompletedLockTokens.Count);
			for (int i = 0; i < incomingMessages.Length; i++)
			{
				Assert.AreEqual(incomingMessages[i].LockToken, mockSession.CompletedLockTokens[i]);

				var currentBody = incomingMessages[i].Body;
				Assert.AreEqual(currentBody.SyncJobRowKey, mergedMembership.SyncJobRowKey);
				Assert.AreEqual(currentBody.SyncJobPartitionKey, mergedMembership.SyncJobPartitionKey);
				Assert.AreEqual(currentBody.RunId, mergedMembership.RunId);
				Assert.AreEqual(currentBody.Destination, mergedMembership.Destination);
				CollectionAssert.AreEqual(currentBody.Sources, mergedMembership.Sources);
			}
		}

		[TestMethod]
		public void IgnoresMissingDestinationGroup()
		{
			var mockGroups = new MockGraphGroupRepository();
			var mockSyncJobs = new MockSyncJobRepository();
			var mockLogs = new MockLoggingRepository();
			var mockMails = new MockMailRepository();
			var updater = new GraphUpdaterApplication(new MembershipDifferenceCalculator<AzureADUser>(), mockGroups, mockSyncJobs, mockLogs, mockMails);
			var sessionCollector = new SessionMessageCollector(updater, mockLogs);

			var mockSession = new MockMessageSession()
			{
				SessionId = "someId"
			};

			var syncJobKeys = (Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

			var syncJob = new SyncJob(syncJobKeys.Item1, syncJobKeys.Item2)
			{
				Enabled = true,
				Status = "InProgress",
			};
			mockSyncJobs.ExistingSyncJobs.Add(syncJobKeys, syncJob);

			var incomingMessages = MakeMembershipMessages();

			foreach (var message in incomingMessages)
			{
				message.Body.SyncJobPartitionKey = syncJobKeys.Item1;
				message.Body.SyncJobRowKey = syncJobKeys.Item2;
			}

			foreach (var message in incomingMessages.SkipLast(1))
			{
				sessionCollector.HandleNewMessage(message, mockSession);

				// sessionCollector doesn't do anything until it gets the last message.
				Assert.AreEqual(0, mockLogs.MessagesLogged);
				Assert.IsFalse(mockSession.Closed);
				Assert.AreEqual(0, mockSession.CompletedLockTokens.Count);
			}

			sessionCollector.HandleNewMessage(incomingMessages.Last(), mockSession);

			Assert.IsTrue(mockSession.Closed);
			Assert.AreEqual(7, mockLogs.MessagesLogged);
			Assert.AreEqual("Error", syncJob.Status);
			Assert.IsFalse(syncJob.Enabled);
			Assert.AreEqual(0, mockGroups.GroupsToUsers.Count);
		}

		[TestMethod]
		public void SyncsGroupsCorrectly()
		{
			var mockGroups = new MockGraphGroupRepository();
			var mockSyncJobs = new MockSyncJobRepository();
			var mockLogs = new MockLoggingRepository();
			var mockMails = new MockMailRepository();
			var updater = new GraphUpdaterApplication(new MembershipDifferenceCalculator<AzureADUser>(), mockGroups, mockSyncJobs, mockLogs, mockMails);
			var sessionCollector = new SessionMessageCollector(updater, mockLogs);

			var mockSession = new MockMessageSession()
			{
				SessionId = "someId"
			};

			var syncJobKeys = (Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

			var syncJob = new SyncJob(syncJobKeys.Item1, syncJobKeys.Item2)
			{
				Enabled = true,
				Status = "InProgress",
			};
			mockSyncJobs.ExistingSyncJobs.Add(syncJobKeys, syncJob);

			var incomingMessages = MakeMembershipMessages();

			mockGroups.GroupsToUsers.Add(incomingMessages.First().Body.Destination.ObjectId, new List<AzureADUser>() { new AzureADUser { ObjectId = Guid.NewGuid() } });

			foreach (var message in incomingMessages)
			{
				message.Body.SyncJobPartitionKey = syncJobKeys.Item1;
				message.Body.SyncJobRowKey = syncJobKeys.Item2;
			}

			foreach (var message in incomingMessages.SkipLast(1))
			{
				sessionCollector.HandleNewMessage(message, mockSession);

				// sessionCollector doesn't do anything until it gets the last message.
				Assert.AreEqual(0, mockLogs.MessagesLogged);
				Assert.IsFalse(mockSession.Closed);
				Assert.AreEqual(0, mockSession.CompletedLockTokens.Count);
			}

			sessionCollector.HandleNewMessage(incomingMessages.Last(), mockSession);

			Assert.IsTrue(mockSession.Closed);
			Assert.AreEqual(9, mockLogs.MessagesLogged);
			Assert.AreEqual("Idle", syncJob.Status);
			Assert.IsTrue(syncJob.Enabled);
			Assert.AreEqual(1, mockGroups.GroupsToUsers.Count);
			Assert.AreEqual(MockGroupMembershipHelper.UserCount, mockGroups.GroupsToUsers.Values.Single().Count);
		}

		[TestMethod]
		public void HandlesErroredJobs()
		{
			var mockGroups = new MockGraphGroupRepository();
			var mockSyncJobs = new MockSyncJobRepository();
			var mockLogs = new MockLoggingRepository();
			var mockMails = new MockMailRepository();
			var updater = new GraphUpdaterApplication(new MembershipDifferenceCalculator<AzureADUser>(), mockGroups, mockSyncJobs, mockLogs, mockMails);
			var sessionCollector = new SessionMessageCollector(updater, mockLogs);

			var mockSession = new MockMessageSession()
			{
				SessionId = "someId"
			};

			var syncJobKeys = (Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

			var syncJob = new SyncJob(syncJobKeys.Item1, syncJobKeys.Item2)
			{
				Enabled = true,
				Status = "InProgress",
			};
			mockSyncJobs.ExistingSyncJobs.Add(syncJobKeys, syncJob);

			var incomingMessage = new GroupMembershipMessage
			{
				LockToken = "hi",
				Body = new Entities.ServiceBus.GroupMembership
				{
					Errored = true,
					Sources = new[] { new AzureADGroup { ObjectId = Guid.NewGuid() } },
					Destination = new AzureADGroup { ObjectId = Guid.NewGuid() },
					IsLastMessage = true,
					RunId = Guid.NewGuid(),
					SourceMembers = new List<AzureADUser>(),
					SyncJobPartitionKey = syncJobKeys.Item1.ToString(),
					SyncJobRowKey = syncJobKeys.Item2.ToString()
				}
			};

			mockGroups.GroupsToUsers.Add(incomingMessage.Body.Destination.ObjectId, new List<AzureADUser>() { new AzureADUser { ObjectId = Guid.NewGuid() } });

			sessionCollector.HandleNewMessage(incomingMessage, mockSession);

			Assert.IsTrue(mockSession.Closed);
			Assert.AreEqual("Error", syncJob.Status);
			Assert.IsFalse(syncJob.Enabled);
			Assert.AreEqual(1, mockGroups.GroupsToUsers.Count);
			Assert.AreEqual(1, mockGroups.GroupsToUsers.Values.Single().Count);
		}


		public GroupMembershipMessage[] MakeMembershipMessages()
		{
			int messageNumber = 0;
			return MockGroupMembershipHelper.MockGroupMembership().Split().Select(x => new GroupMembershipMessage
			{
				Body = x,
				LockToken = (messageNumber++).ToString()
			}).ToArray();
		}
	}
}
