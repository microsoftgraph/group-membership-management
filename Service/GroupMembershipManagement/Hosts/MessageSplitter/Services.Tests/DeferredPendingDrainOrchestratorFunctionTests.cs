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
        public async Task RunAsync_DoesNotReleaseLease_WhenDeferredMessageNotFound()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";

            var runId = Guid.NewGuid();
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
                    new DeferredPendingItem(sequenceNumber, runId, new DateTimeOffset(utcNow, TimeSpan.Zero))
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
                    ShouldRemoveFromIndex: true,
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

            // Lease was acquired, but since the deferred message was not found we do NOT release the lease here.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                runId,
                It.IsAny<CallEntityOptions>()), Times.Never());

            // We still remove the index entry so it doesn't live forever.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Sanity: we did not mark as dispatched.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.MarkDispatched),
                It.IsAny<MarkDeferredPendingDispatchedRequest>(),
                It.IsAny<CallEntityOptions>()), Times.Never());
        }
    }
}
