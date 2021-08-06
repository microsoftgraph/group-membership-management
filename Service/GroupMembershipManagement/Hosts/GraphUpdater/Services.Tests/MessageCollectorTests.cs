// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GraphUpdater;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Services.Tests.Mocks;
using Repositories.Mocks;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class MessageCollectorTests
    {
        [TestMethod]
        public async Task AccumulatesMessages()
        {
            var mockLogs = new MockLoggingRepository();
            var sessionCollector = new MessageCollector(mockLogs);

            var mockSession = new MockMessageSession()
            {
                SessionId = "someId"
            };
            var sessionId = "someId";

            var incomingMessages = MakeMembershipMessages();

            foreach (var message in incomingMessages.SkipLast(1))
            {
                var result = await sessionCollector.HandleNewMessageAsync(message, sessionId);

                // sessionCollector doesn't do anything until it gets the last message.
                Assert.IsFalse(mockSession.Closed);
                Assert.AreEqual(0, mockSession.CompletedLockTokens.Count);
                Assert.AreEqual(false, result.ShouldCompleteMessage);
            }

            var groupMembershipMessageResponse = await sessionCollector.HandleNewMessageAsync(incomingMessages.Last(), sessionId);
            var mergedMembership = groupMembershipMessageResponse.CompletedGroupMembershipMessages.Select(x => x.Body).FirstOrDefault();

            Assert.IsFalse(mockSession.Closed);
            Assert.IsTrue(groupMembershipMessageResponse.ShouldCompleteMessage);
            Assert.AreEqual(incomingMessages.Length, groupMembershipMessageResponse.CompletedGroupMembershipMessages.Count);

            for (int i = 0; i < incomingMessages.Length; i++)
            {
                var currentBody = incomingMessages[i].Body;
                Assert.AreEqual(currentBody.SyncJobRowKey, mergedMembership.SyncJobRowKey);
                Assert.AreEqual(currentBody.SyncJobPartitionKey, mergedMembership.SyncJobPartitionKey);
                Assert.AreEqual(currentBody.RunId, mergedMembership.RunId);
                Assert.AreEqual(currentBody.Destination, mergedMembership.Destination);
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
