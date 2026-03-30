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
        [TestMethod]
        public async Task RunAsync_ReleasesLeaseAndKeepsIndex_WhenDeferredMessageNotFoundAndNotDispatched()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 123;

            var input = new DeferredPendingDrainRequest(lane);

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                    It.IsAny<TryAcquireDrainLockRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNextBatch),
                    It.IsAny<TakeNextBatchRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(
                [
                    new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                    {
                        Dispatched = false,
                        OrchestrationInstanceId = null
                    }
                ]);

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

            context.Setup(x => x.Entities.CallEntityAsync(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                    null,
                    It.IsAny<CallEntityOptions>()))
                .Returns(Task.CompletedTask);

            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new DeferredPendingDrainOrchestrator(new RunLimiterSettings
            {
                MaxInFlightMessages = 16,
                LeaseTimeoutMinutes = 2,
                HeartbeatIntervalMinutes = 0,
                IsEnabled = true
            });

            await orchestrator.RunAsync(context.Object);

            // We rely on the index entity's own serialization and markers; no explicit entity lock should be taken for it.
            context.Verify(x => x.Entities.LockEntitiesAsync(It.Is<EntityInstanceId>(id => id == indexEntityId)), Times.Never());

            // RunLimiter is also a single entity instance per lane; explicit locks are redundant.
            context.Verify(x => x.Entities.LockEntitiesAsync(It.Is<EntityInstanceId>(id => id == limiterEntityId)), Times.Never());

            // Lease was acquired but nothing was dispatched; release the lease so we don't leak capacity.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                runId,
                It.IsAny<CallEntityOptions>()), Times.Once());

            // We keep the index entry for retry (this can happen if drain runs before defer).
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Sanity: we did not mark as dispatched.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.MarkDispatched),
                It.IsAny<MarkDeferredPendingDispatchedRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_RemovesIndexEntry_WhenDeferredMessageNotFoundAndAlreadyDispatched()
        {
            // This tests the scenario where an item was previously dispatched (orchestration running)
            // but message completion failed. On retry, if MessageNotFound, it's safe to remove.
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 456;
            const string orchestrationInstanceId = "deferredpending_test_456";

            var input = new DeferredPendingDrainRequest(lane);

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                    It.IsAny<TryAcquireDrainLockRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            // Item was already dispatched in a previous drain attempt
            context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNextBatch),
                    It.IsAny<TakeNextBatchRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(
                [
                    new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                    {
                        Dispatched = true,
                        OrchestrationInstanceId = orchestrationInstanceId
                    }
                ]);

            // No lease acquisition needed since already dispatched
            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: true,
                    ShouldRemoveFromIndex: true,  // Safe to remove since already dispatched
                    OrchestrationInstanceId: orchestrationInstanceId,
                    MessageNotFound: true));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.Remove),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                    null,
                    It.IsAny<CallEntityOptions>()))
                .Returns(Task.CompletedTask);

            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new DeferredPendingDrainOrchestrator(new RunLimiterSettings
            {
                MaxInFlightMessages = 16,
                LeaseTimeoutMinutes = 2,
                HeartbeatIntervalMinutes = 0,
                IsEnabled = true
            });

            await orchestrator.RunAsync(context.Object);

            // No lease should be acquired (item already dispatched)
            context.Verify(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                limiterEntityId,
                nameof(RunLimiter.Acquire),
                It.IsAny<AcquireLeaseRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Index entry should be removed (safe because already dispatched)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // ReleaseInProgress should NOT be called (we removed it)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_RemovesStaleEntry_WhenMessageNotFoundAndOlderThanFiveMinutes()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 999;

            var input = new DeferredPendingDrainRequest(lane);

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                    It.IsAny<TryAcquireDrainLockRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            // Item was enqueued 10 minutes ago — well past the 5-minute stale threshold
            context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNextBatch),
                    It.IsAny<TakeNextBatchRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(
                [
                    new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(-10), jobId)
                    {
                        Dispatched = false,
                        OrchestrationInstanceId = null
                    }
                ]);

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

            context.Setup(x => x.Entities.CallEntityAsync(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                    null,
                    It.IsAny<CallEntityOptions>()))
                .Returns(Task.CompletedTask);

            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new DeferredPendingDrainOrchestrator(new RunLimiterSettings
            {
                MaxInFlightMessages = 16,
                LeaseTimeoutMinutes = 2,
                HeartbeatIntervalMinutes = 0,
                IsEnabled = true
            });

            await orchestrator.RunAsync(context.Object);

            // Stale entry should be REMOVED (age > 5 min, message not found, not dispatched)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // ReleaseInProgress should NOT be called (we removed it)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Lease was acquired but nothing dispatched — should be released
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                runId,
                It.IsAny<CallEntityOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_KeepsEntry_WhenMessageNotFoundAndExactlyFiveMinutesOld()
        {
            // Boundary: item at exactly 5.0 minutes should NOT be removed (> 5, not >=)
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 555;

            var input = new DeferredPendingDrainRequest(lane);

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                    It.IsAny<TryAcquireDrainLockRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            // Enqueued exactly 5 minutes ago
            context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNextBatch),
                    It.IsAny<TakeNextBatchRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(
                [
                    new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(-5), jobId)
                    {
                        Dispatched = false,
                        OrchestrationInstanceId = null
                    }
                ]);

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

            context.Setup(x => x.Entities.CallEntityAsync(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                    null,
                    It.IsAny<CallEntityOptions>()))
                .Returns(Task.CompletedTask);

            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new DeferredPendingDrainOrchestrator(new RunLimiterSettings
            {
                MaxInFlightMessages = 16,
                LeaseTimeoutMinutes = 2,
                HeartbeatIntervalMinutes = 0,
                IsEnabled = true
            });

            await orchestrator.RunAsync(context.Object);

            // Exactly 5 minutes is NOT stale (> 5, not >=). Entry should be kept.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Lease was acquired but nothing dispatched — must be released
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                runId,
                It.IsAny<CallEntityOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_ExitsEarly_WhenDrainLockNotAcquired()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var input = new DeferredPendingDrainRequest(lane);

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                    It.IsAny<TryAcquireDrainLockRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(false);

            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new DeferredPendingDrainOrchestrator(new RunLimiterSettings
            {
                MaxInFlightMessages = 16,
                LeaseTimeoutMinutes = 2,
                HeartbeatIntervalMinutes = 0,
                IsEnabled = true
            });

            await orchestrator.RunAsync(context.Object);

            // Should never attempt to take a batch
            context.Verify(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.TakeNextBatch),
                It.IsAny<TakeNextBatchRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Should NOT release drain lock (it was never acquired)
            context.Verify(x => x.Entities.CallEntityAsync(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                null,
                It.IsAny<CallEntityOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_MarksCapacityDeniedAndReturns_WhenNoLeaseAvailable()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 700;

            var input = new DeferredPendingDrainRequest(lane);

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                    It.IsAny<TryAcquireDrainLockRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNextBatch),
                    It.IsAny<TakeNextBatchRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(
                [
                    new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                    {
                        Dispatched = false,
                        OrchestrationInstanceId = null
                    }
                ]);

            // Capacity denied
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

            context.Setup(x => x.Entities.CallEntityAsync(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                    null,
                    It.IsAny<CallEntityOptions>()))
                .Returns(Task.CompletedTask);

            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new DeferredPendingDrainOrchestrator(new RunLimiterSettings
            {
                MaxInFlightMessages = 16,
                LeaseTimeoutMinutes = 2,
                HeartbeatIntervalMinutes = 0,
                IsEnabled = true
            });

            await orchestrator.RunAsync(context.Object);

            // Should call MarkCapacityDeniedAndRelease with correct sequence number and timestamp
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.MarkCapacityDeniedAndRelease),
                It.Is<MarkCapacityDeniedRequest>(r => r.SequenceNumber == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Should NOT call ReceiveDeferredPendingFunction (no capacity = no dispatch attempt)
            context.Verify(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                nameof(ReceiveDeferredPendingFunction),
                It.IsAny<ReceiveDeferredPendingRequest>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // Drain lock should still be released in finally block
            context.Verify(x => x.Entities.CallEntityAsync(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                null,
                It.IsAny<CallEntityOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_ReleasesLeaseAndInProgress_WhenReceiveActivityThrows()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 800;

            var input = new DeferredPendingDrainRequest(lane);

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                    It.IsAny<TryAcquireDrainLockRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNextBatch),
                    It.IsAny<TakeNextBatchRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(
                [
                    new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                    {
                        Dispatched = false,
                        OrchestrationInstanceId = null
                    }
                ]);

            // Lease acquired
            context.Setup(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                    limiterEntityId,
                    nameof(RunLimiter.Acquire),
                    It.IsAny<AcquireLeaseRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new AcquireLeaseResponse(true, 1, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(2)));

            // ReceiveDeferredPendingFunction throws
            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ThrowsAsync(new InvalidOperationException("Service Bus transient failure"));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    limiterEntityId,
                    nameof(RunLimiter.Release),
                    runId,
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                    null,
                    It.IsAny<CallEntityOptions>()))
                .Returns(Task.CompletedTask);

            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new DeferredPendingDrainOrchestrator(new RunLimiterSettings
            {
                MaxInFlightMessages = 16,
                LeaseTimeoutMinutes = 2,
                HeartbeatIntervalMinutes = 0,
                IsEnabled = true
            });

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => orchestrator.RunAsync(context.Object));

            // Lease should be released (no work dispatched)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                runId,
                It.IsAny<CallEntityOptions>()), Times.Once());

            // In-progress marker should be released
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Drain lock should still be released in the finally block
            context.Verify(x => x.Entities.CallEntityAsync(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                null,
                It.IsAny<CallEntityOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_ProcessesMultipleItems_FirstDispatchedSecondStale()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var runId1 = Guid.NewGuid();
            var jobId1 = Guid.NewGuid();
            var runId2 = Guid.NewGuid();
            var jobId2 = Guid.NewGuid();
            const long seq1 = 100;
            const long seq2 = 200;

            var input = new DeferredPendingDrainRequest(lane);

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                    It.IsAny<TryAcquireDrainLockRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            // Two items: first is fresh, second is stale (15 min old)
            context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNextBatch),
                    It.IsAny<TakeNextBatchRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(
                [
                    new DeferredPendingItem(seq1, runId1, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId1)
                    {
                        Dispatched = false,
                        OrchestrationInstanceId = null
                    },
                    new DeferredPendingItem(seq2, runId2, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(-15), jobId2)
                    {
                        Dispatched = false,
                        OrchestrationInstanceId = null
                    }
                ]);

            // Both items acquire leases
            context.Setup(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                    limiterEntityId,
                    nameof(RunLimiter.Acquire),
                    It.IsAny<AcquireLeaseRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new AcquireLeaseResponse(true, 1, new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(2)));

            // First item: dispatched successfully
            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.Is<ReceiveDeferredPendingRequest>(r => r.SequenceNumber == seq1),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: true,
                    ShouldRemoveFromIndex: true,
                    OrchestrationInstanceId: "instance_100",
                    MessageNotFound: false));

            // Second item: message not found (stale)
            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.Is<ReceiveDeferredPendingRequest>(r => r.SequenceNumber == seq2),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: false,
                    ShouldRemoveFromIndex: false,
                    OrchestrationInstanceId: null,
                    MessageNotFound: true));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.MarkDispatched),
                    It.IsAny<MarkDeferredPendingDispatchedRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.Remove),
                    It.IsAny<long>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                    null,
                    It.IsAny<CallEntityOptions>()))
                .Returns(Task.CompletedTask);

            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new DeferredPendingDrainOrchestrator(new RunLimiterSettings
            {
                MaxInFlightMessages = 16,
                LeaseTimeoutMinutes = 2,
                HeartbeatIntervalMinutes = 0,
                IsEnabled = true
            });

            await orchestrator.RunAsync(context.Object);

            // Both items should be removed (first dispatched+completed, second stale)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == seq1),
                It.IsAny<CallEntityOptions>()), Times.Once());

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == seq2),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // First item was dispatched — lease should NOT be released
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                runId1,
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Second item was not dispatched — lease SHOULD be released
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                runId2,
                It.IsAny<CallEntityOptions>()), Times.Once());

            // First item should be marked dispatched
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.MarkDispatched),
                It.Is<MarkDeferredPendingDispatchedRequest>(r => r.SequenceNumber == seq1),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // ReleaseInProgress should NOT be called for either (both removed)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_DispatchesAndRemoves_WhenMessageFoundSuccessfully()
        {
            // Happy path: message is found, dispatched, and completed successfully
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 789;
            const string orchestrationInstanceId = "deferredpending_test_789";

            var input = new DeferredPendingDrainRequest(lane);

            var context = new Mock<TaskOrchestrationContext> { DefaultValue = DefaultValue.Mock };
            context.Setup(x => x.GetInput<DeferredPendingDrainRequest>()).Returns(input);
            context.SetupGet(x => x.CurrentUtcDateTime).Returns(utcNow);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                    It.IsAny<TryAcquireDrainLockRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNextBatch),
                    It.IsAny<TakeNextBatchRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(
                [
                    new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero), jobId)
                    {
                        Dispatched = false,
                        OrchestrationInstanceId = null
                    }
                ]);

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
                    ShouldRemoveFromIndex: true,
                    OrchestrationInstanceId: orchestrationInstanceId,
                    MessageNotFound: false));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.MarkDispatched),
                    It.IsAny<MarkDeferredPendingDispatchedRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.Remove),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseDrainLock),
                    null,
                    It.IsAny<CallEntityOptions>()))
                .Returns(Task.CompletedTask);

            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            var orchestrator = new DeferredPendingDrainOrchestrator(new RunLimiterSettings
            {
                MaxInFlightMessages = 16,
                LeaseTimeoutMinutes = 2,
                HeartbeatIntervalMinutes = 0,
                IsEnabled = true
            });

            await orchestrator.RunAsync(context.Object);

            // Lease should be acquired
            context.Verify(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                limiterEntityId,
                nameof(RunLimiter.Acquire),
                It.IsAny<AcquireLeaseRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Should mark as dispatched
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.MarkDispatched),
                It.IsAny<MarkDeferredPendingDispatchedRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Lease should NOT be released (work was dispatched, orchestration is running)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.IsAny<Guid>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // Index entry should be removed (message completed)
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());
        }
    }
}
