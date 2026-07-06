// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Hosts.MessageSplitter;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ReceiveDeferredPendingFunction"/>, focused on the race-free
    /// peer-orchestration probe that authoritatively distinguishes a genuine orphan from a benign
    /// double-drain artifact when the deferred message is "not found".
    /// </summary>
    [TestClass]
    public class ReceiveDeferredPendingFunctionTests
    {
        private Mock<ServiceBusReceiver> _receiver;
        private Mock<DurableTaskClient> _durableClient;

        [TestInitialize]
        public void Setup()
        {
            _receiver = new Mock<ServiceBusReceiver>();
            _durableClient = new Mock<DurableTaskClient>("test");

            _receiver
                .Setup(x => x.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        private ReceiveDeferredPendingFunction CreateFunction() =>
            new ReceiveDeferredPendingFunction(_receiver.Object, NullLogger<ReceiveDeferredPendingFunction>.Instance);

        private static string DeterministicInstanceId(Guid runId, long sequenceNumber) =>
            $"deferredpending_{runId}_{sequenceNumber}";

        [TestMethod]
        public async Task RunAsync_ReportsPeerOrchestrationExists_WhenMessageNotFound_AndInstancePresent()
        {
            var runId = Guid.NewGuid();
            const long seq = 123;
            var instanceId = DeterministicInstanceId(runId, seq);

            _receiver
                .Setup(x => x.ReceiveDeferredMessageAsync(seq, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceBusException("not found", ServiceBusFailureReason.MessageNotFound));

            // A peer already dispatched the deterministic GraphUpdater orchestration for this exact item.
            _durableClient
                .Setup(x => x.GetInstanceAsync(instanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrchestrationMetadata(nameof(OrchestratorFunction), instanceId));

            var request = new ReceiveDeferredPendingRequest(seq, runId, AlreadyDispatched: false, OrchestrationInstanceId: "orig");
            var response = await CreateFunction().RunAsync(request, _durableClient.Object);

            Assert.IsTrue(response.MessageNotFound, "Expected MessageNotFound to be surfaced.");
            Assert.IsTrue(response.PeerOrchestrationExists, "Peer orchestration exists, so this is never a genuine orphan.");
            Assert.IsFalse(response.ShouldRemoveFromIndex, "A stale (not-already-dispatched) loser must not remove the entry.");
            Assert.IsFalse(response.Dispatched);
        }

        [TestMethod]
        public async Task RunAsync_ReportsNoPeerOrchestration_WhenMessageNotFound_AndInstanceAbsent()
        {
            var runId = Guid.NewGuid();
            const long seq = 456;
            var instanceId = DeterministicInstanceId(runId, seq);

            _receiver
                .Setup(x => x.ReceiveDeferredMessageAsync(seq, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceBusException("not found", ServiceBusFailureReason.MessageNotFound));

            // No peer orchestration exists — the orchestrator must fall back to the entity orphan check.
            _durableClient
                .Setup(x => x.GetInstanceAsync(instanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrchestrationMetadata)null);

            var request = new ReceiveDeferredPendingRequest(seq, runId, AlreadyDispatched: false, OrchestrationInstanceId: "orig");
            var response = await CreateFunction().RunAsync(request, _durableClient.Object);

            Assert.IsTrue(response.MessageNotFound);
            Assert.IsFalse(response.PeerOrchestrationExists, "No instance exists, so the probe must not claim a peer dispatched it.");
            Assert.IsFalse(response.ShouldRemoveFromIndex);
        }

        [TestMethod]
        public async Task RunAsync_SkipsPeerProbe_WhenMessageNotFound_ButAlreadyDispatched()
        {
            var runId = Guid.NewGuid();
            const long seq = 789;

            _receiver
                .Setup(x => x.ReceiveDeferredMessageAsync(seq, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceBusException("not found", ServiceBusFailureReason.MessageNotFound));

            // This drain already dispatched — it is not a stale loser, so no probe is needed and the entry is removed.
            var request = new ReceiveDeferredPendingRequest(seq, runId, AlreadyDispatched: true, OrchestrationInstanceId: "orig");
            var response = await CreateFunction().RunAsync(request, _durableClient.Object);

            Assert.IsTrue(response.MessageNotFound);
            Assert.IsFalse(response.PeerOrchestrationExists, "AlreadyDispatched short-circuits the probe to false.");
            Assert.IsTrue(response.ShouldRemoveFromIndex, "An already-dispatched drain removes its own index entry.");
            Assert.IsTrue(response.Dispatched);

            _durableClient.Verify(
                x => x.GetInstanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
                Times.Never(),
                "The peer probe must be skipped entirely when AlreadyDispatched is true.");
        }

        [TestMethod]
        public async Task RunAsync_ReportsPeerOrchestrationExists_WhenMessageNull_AndInstancePresent()
        {
            var runId = Guid.NewGuid();
            const long seq = 321;
            var instanceId = DeterministicInstanceId(runId, seq);

            _receiver
                .Setup(x => x.ReceiveDeferredMessageAsync(seq, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ServiceBusReceivedMessage)null);

            _durableClient
                .Setup(x => x.GetInstanceAsync(instanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrchestrationMetadata(nameof(OrchestratorFunction), instanceId));

            var request = new ReceiveDeferredPendingRequest(seq, runId, AlreadyDispatched: false, OrchestrationInstanceId: "orig");
            var response = await CreateFunction().RunAsync(request, _durableClient.Object);

            Assert.IsTrue(response.MessageNotFound, "A null message is treated as not-found.");
            Assert.IsTrue(response.PeerOrchestrationExists);
        }

        [TestMethod]
        public async Task RunAsync_DispatchesWithDeterministicId_AndCompletes_WhenMessageReceived()
        {
            var runId = Guid.NewGuid();
            var syncJobId = Guid.NewGuid();
            const long seq = 555;
            var instanceId = DeterministicInstanceId(runId, seq);

            var orchestratorRequest = new OrchestratorRequest
            {
                CurrentLaneSize = "Small",
                MessageId = Guid.NewGuid().ToString(),
                SubscriptionName = "Small",
                UpdaterType = "GroupMembership",
                MembershipRequest = new MembershipHttpRequest
                {
                    SyncJob = new SyncJob { Id = syncJobId, RunId = runId },
                    FilePath = "/file.json",
                    ProjectedMemberCount = 1,
                    MembersToBeAdded = 1,
                    MembersToBeRemoved = 0,
                    GroupId = Guid.NewGuid()
                }
            };

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(orchestratorRequest));
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(new BinaryData(bytes), sequenceNumber: seq);

            _receiver
                .Setup(x => x.ReceiveDeferredMessageAsync(seq, It.IsAny<CancellationToken>()))
                .ReturnsAsync(message);

            // No pre-existing instance — the activity must schedule one under the deterministic id.
            _durableClient
                .Setup(x => x.GetInstanceAsync(instanceId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrchestrationMetadata)null);
            _durableClient
                .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(
                    It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<StartOrchestrationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(instanceId);

            var request = new ReceiveDeferredPendingRequest(seq, runId, AlreadyDispatched: false, OrchestrationInstanceId: "orig");
            var response = await CreateFunction().RunAsync(request, _durableClient.Object);

            Assert.IsTrue(response.Dispatched);
            Assert.IsTrue(response.ShouldRemoveFromIndex, "A completed message removes its index entry.");
            Assert.IsFalse(response.MessageNotFound);
            Assert.AreEqual(instanceId, response.OrchestrationInstanceId, "The response must carry the deterministic instance id.");

            _durableClient.Verify(
                x => x.ScheduleNewOrchestrationInstanceAsync(
                    It.Is<TaskName>(t => t.Name == nameof(OrchestratorFunction)),
                    It.IsAny<object>(),
                    It.Is<StartOrchestrationOptions>(o => o.InstanceId == instanceId),
                    It.IsAny<CancellationToken>()),
                Times.Once(),
                "The orchestration must be scheduled with the deterministic instance id so retries dedup.");

            _receiver.Verify(x => x.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once());
        }
    }
}
