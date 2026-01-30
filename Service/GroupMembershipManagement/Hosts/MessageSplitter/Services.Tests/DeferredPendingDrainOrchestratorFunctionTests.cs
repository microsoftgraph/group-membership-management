// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
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

            context.Setup(x => x.CallActivityAsync(
                    nameof(LoggerFunction),
                    It.IsAny<LoggerRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);

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

            context.Setup(x => x.CallActivityAsync(
                    nameof(LoggerFunction),
                    It.IsAny<LoggerRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);

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

            context.Setup(x => x.CallActivityAsync(
                    nameof(LoggerFunction),
                    It.IsAny<LoggerRequest>(),
                    It.IsAny<TaskOptions>()))
                .Returns(Task.CompletedTask);

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
