// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.ServiceBusTopics.Tests
{
    [TestClass]
    public class ServiceBusTopicsRepositoryTests
    {
        private Mock<ServiceBusSender> _sender = null!;
        private List<ServiceBusMessage> _sent = null!;
        private ServiceBusTopicsRepository _repository = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sent = new List<ServiceBusMessage>();
            _sender = new Mock<ServiceBusSender>();
            _sender
                .Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
                .Callback<ServiceBusMessage, CancellationToken>((m, _) => _sent.Add(m))
                .Returns(Task.CompletedTask);
            _repository = new ServiceBusTopicsRepository(_sender.Object);
        }

        private static SyncJob JobWithQuery(string query) => new SyncJob
        {
            PartitionKey = "pk",
            RowKey = "rk",
            RunId = Guid.NewGuid(),
            Query = query,
            MembershipType = "GroupMembership"
        };

        [TestMethod]
        public async Task AddMessageAsync_GroupMembershipQuery_PrimitiveSource_DoesNotThrow()
        {
            // Regression (sev-1): a normal query part's "source" is a bare GUID string. Parsing must
            // type-check for JsonObject before indexing — indexing a JsonValue with ["groupId"] throws
            // InvalidOperationException, which previously crashed AddMessageAsync for every non-channel job.
            var job = JobWithQuery("[{\"type\":\"SecurityGroup\",\"source\":\"11111111-1111-1111-1111-111111111111\"}]");

            await _repository.AddMessageAsync(job);

            Assert.AreEqual(2, _sent.Count); // one source part + one destination part
            Assert.IsFalse(_sent[0].ApplicationProperties.ContainsKey("SourceChannelId"));
            Assert.IsFalse(_sent[0].ApplicationProperties.ContainsKey("SourceGroupId"));
        }

        [TestMethod]
        public async Task AddMessageAsync_MultipleGroupSources_DoesNotThrow()
        {
            var job = JobWithQuery("[{\"type\":\"SecurityGroup\",\"source\":\"11111111-1111-1111-1111-111111111111\"},{\"type\":\"SecurityGroup\",\"exclusionary\":true,\"source\":\"33333333-3333-3333-3333-333333333333\"}]");

            await _repository.AddMessageAsync(job);

            Assert.AreEqual(3, _sent.Count); // two source parts + one destination part
        }

        [TestMethod]
        public async Task AddMessageAsync_TeamsChannelSourcePart_ForwardsSourceIdentity()
        {
            var job = JobWithQuery("[{\"type\":\"TeamsChannelMembership\",\"source\":{\"groupId\":\"22222222-2222-2222-2222-222222222222\",\"channelId\":\"19:abc@thread.tacv2\"}}]");

            await _repository.AddMessageAsync(job);

            var sourcePart = _sent[0];
            Assert.AreEqual("22222222-2222-2222-2222-222222222222", sourcePart.ApplicationProperties["SourceGroupId"]);
            Assert.AreEqual("19:abc@thread.tacv2", sourcePart.ApplicationProperties["SourceChannelId"]);
        }
    }
}
