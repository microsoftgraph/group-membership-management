// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;

namespace Services.Tests
{
    [TestClass]
    public class DeferredPendingDrainOrchestratorFunctionTests
    {
        private static RunLimiterSettings DefaultSettings => new RunLimiterSettings
        {
            MaxInFlightMessages = 16,
            LeaseTimeoutMinutes = 2,
            HeartbeatIntervalMinutes = 0,
            IsEnabled = true
        };

        private static Mock<TaskOrchestrationContext> CreateContext(
            DateTime utcNow, string lane, out EntityInstanceId indexEntityId, out EntityInstanceId limiterEntityId)
        {
            var input = new DeferredPendingDrainRequest(lane);
            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);
            return context;
        }

        [TestMethod]
        public async Task RunAsync_ReturnsEarly_WhenNoItemsAvailable()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var context = CreateContext(utcNow, lane, out var indexEntityId, out _);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync((DeferredPendingItem)null);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // Should never attempt to acquire a lease or call the activity
            context.Verify(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                It.IsAny<EntityInstanceId>(),
                nameof(RunLimiter.Acquire),
                It.IsAny<AcquireLeaseRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            context.Verify(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                nameof(ReceiveDeferredPendingFunction),
                It.IsAny<ReceiveDeferredPendingRequest>(),
                It.IsAny<TaskOptions>()), Times.Never());

            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_DispatchesItemAndContinues_WhenLeaseAcquiredAndDispatched()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 100;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                {
                    Dispatched = false,
                    OrchestrationInstanceId = null
                });

            context.Setup(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                    limiterEntityId,
                    nameof(RunLimiter.Acquire),
                    It.IsAny<AcquireLeaseRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new AcquireLeaseResponse(true, 1, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(2)));

            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: true,
                    ShouldRemoveFromIndex: false,
                    OrchestrationInstanceId: "orch-123",
                    MessageNotFound: false));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.MarkDispatched),
                    It.IsAny<MarkDeferredPendingDispatchedRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // Verified: dispatch happened
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.MarkDispatched),
                It.Is<MarkDeferredPendingDispatchedRequest>(r => r.SequenceNumber == sequenceNumber && r.OrchestrationInstanceId == "orch-123"),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Newly dispatched = forward progress → ContinueAsNew
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Once());

            // Lease should NOT be released (item was dispatched)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.IsAny<Guid>(),
                It.IsAny<CallEntityOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_StopsWithoutContinue_WhenCapacityDenied()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 700;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                {
                    Dispatched = false,
                    OrchestrationInstanceId = null
                });

            context.Setup(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                    limiterEntityId,
                    nameof(RunLimiter.Acquire),
                    It.IsAny<AcquireLeaseRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new AcquireLeaseResponse(false, 16, null));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.MarkCapacityDeniedAndRelease),
                    It.IsAny<MarkCapacityDeniedRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // Should mark capacity denied
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.MarkCapacityDeniedAndRelease),
                It.Is<MarkCapacityDeniedRequest>(r => r.SequenceNumber == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Should NOT call activity
            context.Verify(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                nameof(ReceiveDeferredPendingFunction),
                It.IsAny<ReceiveDeferredPendingRequest>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // Should NOT continue
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_RemovesAndContinues_WhenShouldRemoveFromIndex()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 200;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            // Already-dispatched item whose orchestration completed
            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                {
                    Dispatched = true,
                    OrchestrationInstanceId = "orch-done"
                });

            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: true,
                    ShouldRemoveFromIndex: true,
                    OrchestrationInstanceId: "orch-done",
                    MessageNotFound: false));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.Remove),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // Should skip lease acquisition (already dispatched)
            context.Verify(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                It.IsAny<EntityInstanceId>(),
                nameof(RunLimiter.Acquire),
                It.IsAny<AcquireLeaseRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Should remove from index
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Removal = forward progress → ContinueAsNew
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_RemovesStaleEntryAndContinues_WhenMessageNotFoundOlderThan5Min()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 300;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            // Item enqueued 10 minutes ago
            var enqueuedAt = new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(-10);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, enqueuedAt, jobId)
                {
                    Dispatched = false,
                    OrchestrationInstanceId = null
                });

            context.Setup(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                    limiterEntityId,
                    nameof(RunLimiter.Acquire),
                    It.IsAny<AcquireLeaseRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new AcquireLeaseResponse(true, 1, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(2)));

            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: false,
                    ShouldRemoveFromIndex: false,
                    OrchestrationInstanceId: null,
                    MessageNotFound: true));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.Remove),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // Should remove stale entry
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Should release lease (no work dispatched)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.Is<Guid>(r => r == runId),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Stale removal = forward progress → ContinueAsNew
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_KeepsItemWithoutContinue_WhenMessageNotFoundUnder5Min()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 400;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            // Item enqueued just now (< 5 min ago)
            var enqueuedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, enqueuedAt, jobId)
                {
                    Dispatched = false,
                    OrchestrationInstanceId = null
                });

            context.Setup(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                    limiterEntityId,
                    nameof(RunLimiter.Acquire),
                    It.IsAny<AcquireLeaseRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new AcquireLeaseResponse(true, 1, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(2)));

            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: false,
                    ShouldRemoveFromIndex: false,
                    OrchestrationInstanceId: null,
                    MessageNotFound: true));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // Should keep in index (release, not remove)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Should release lease (no work dispatched)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.Is<Guid>(r => r == runId),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // No forward progress → should NOT continue
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_KeepsItemWithoutContinue_WhenAlreadyDispatchedAndStillRunning()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "large";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 500;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out _);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                {
                    Dispatched = true,
                    OrchestrationInstanceId = "orch-running"
                });

            // Orchestration still running
            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: true,
                    ShouldRemoveFromIndex: false,
                    OrchestrationInstanceId: "orch-running",
                    MessageNotFound: false));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // Should skip lease acquisition (already dispatched)
            context.Verify(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                It.IsAny<EntityInstanceId>(),
                nameof(RunLimiter.Acquire),
                It.IsAny<AcquireLeaseRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // item.Dispatched == true && received.Dispatched == true → NOT newly dispatched
            // Should release, not remove
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // No forward progress → should NOT continue
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_ReleasesLeaseAndRethrows_WhenReceiveActivityThrows()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 600;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                {
                    Dispatched = false,
                    OrchestrationInstanceId = null
                });

            context.Setup(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                    limiterEntityId,
                    nameof(RunLimiter.Acquire),
                    It.IsAny<AcquireLeaseRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new AcquireLeaseResponse(true, 1, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(2)));

            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ThrowsAsync(new InvalidOperationException("SB receive failed"));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    limiterEntityId,
                    nameof(RunLimiter.Release),
                    It.Is<Guid>(r => r == runId),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => orchestrator.RunAsync(context.Object));

            // Lease should be released on exception
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.Is<Guid>(r => r == runId),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // InProgress should be released on exception
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Should NOT continue on exception
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_ReleasesLeaseAndKeepsItem_WhenMessageNotFoundButRecent()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 123;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            // Enqueued exactly 5 minutes ago (boundary — should NOT remove)
            var enqueuedAt = new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(-5);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, enqueuedAt, jobId)
                {
                    Dispatched = false,
                    OrchestrationInstanceId = null
                });

            context.Setup(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                    limiterEntityId,
                    nameof(RunLimiter.Acquire),
                    It.IsAny<AcquireLeaseRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new AcquireLeaseResponse(true, 1, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(2)));

            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: false,
                    ShouldRemoveFromIndex: false,
                    OrchestrationInstanceId: null,
                    MessageNotFound: true));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // Should keep in index (exactly 5 min is NOT > 5 min)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Should release lease
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.Is<Guid>(r => r == runId),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // No forward progress → no continue
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_DoesNotReleaseLease_WhenAlreadyDispatchedItemIsRemoved()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "large";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 800;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                {
                    Dispatched = true,
                    OrchestrationInstanceId = "orch-completed"
                });

            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: true,
                    ShouldRemoveFromIndex: true,
                    OrchestrationInstanceId: "orch-completed",
                    MessageNotFound: false));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.Remove),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // Should NOT acquire or release lease for already-dispatched items
            context.Verify(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                It.IsAny<EntityInstanceId>(),
                nameof(RunLimiter.Acquire),
                It.IsAny<AcquireLeaseRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.IsAny<Guid>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Should remove and continue
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_DoesNotReleaseLeaseOnException_WhenAlreadyDispatched()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 900;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                {
                    Dispatched = true,
                    OrchestrationInstanceId = "orch-123"
                });

            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ThrowsAsync(new TimeoutException("SB timeout"));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);

            await Assert.ThrowsExceptionAsync<TimeoutException>(
                () => orchestrator.RunAsync(context.Object));

            // No lease was acquired, so none should be released
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.IsAny<Guid>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // InProgress should still be released
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());
        }
    }
}
