// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GraphUpdater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tests.FunctionApps.Mocks;

namespace Tests.FunctionApps
{
	[TestClass]
	public class GraphUpdaterTests
	{
		[TestMethod]
		public void AccumulatesMessagesAndUpdatesGraph()
		{
			var mockUpdater = new MockGraphUpdater();
			var sessionCollector = new SessionMessageCollector(mockUpdater);

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
				Assert.AreEqual(currentBody.Destination, mergedMembership.Destination);
				CollectionAssert.AreEqual(currentBody.Sources, mergedMembership.Sources);
			}
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

