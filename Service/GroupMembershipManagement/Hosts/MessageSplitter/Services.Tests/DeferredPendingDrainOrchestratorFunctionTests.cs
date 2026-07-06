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
            MaxPendingAgeMinutes = 60,
            IsEnabled = true
        };

        private static RunLimiterSettings SettingsWithMaxPendingAge(int maxPendingAgeMinutes) => new RunLimiterSettings
        {
            MaxInFlightMessages = 16,
            LeaseTimeoutMinutes = 2,
            HeartbeatIntervalMinutes = 0,
            MaxPendingAgeMinutes = maxPendingAgeMinutes,
            IsEnabled = true
        };

        // Wires a context where TakeNext yields a single undispatched item of the given age and
        // ReceiveDeferredPending reports the message as not found. Both terminal index operations
        // (Remove for an orphan, ReleaseInProgress for a retry) are stubbed so the orchestrator's
        // age decision selects which one runs; the test asserts the chosen path.
        private static Mock<TaskOrchestrationContext> CreateNotFoundContext(
            DateTime utcNow, string lane, long sequenceNumber, Guid runId, Guid jobId, double ageMinutes,
            out EntityInstanceId indexEntityId, out EntityInstanceId limiterEntityId)
        {
            var context = CreateContext(utcNow, lane, out indexEntityId, out limiterEntityId);
            var idx = indexEntityId;
            var lim = limiterEntityId;
            var enqueuedAt = new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(-ageMinutes);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    idx,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, enqueuedAt, jobId)
                {
                    Dispatched = false,
                    OrchestrationInstanceId = null
                });

            context.Setup(x => x.Entities.CallEntityAsync<AcquireLeaseResponse>(
                    lim,
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
                    idx,
                    nameof(DeferredPendingIndexEntity.Remove),
                    It.IsAny<long>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    idx,
                    nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                    It.IsAny<long>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            // By default a not-found, aged entry is a genuine orphan: the entity still holds the
            // never-dispatched item and confirms it (without removing). Tests modelling the
            // double-drain race override this to false (a peer already dispatched/removed the entry).
            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    idx,
                    nameof(DeferredPendingIndexEntity.IsConfirmedOrphan),
                    It.IsAny<long>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            return context;
        }

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
        public async Task RunAsync_ErrorsAndRemovesConfirmedOrphan_WhenMessageNotFoundAgedPastWindow()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 300;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            // Capture the orchestrator's logger so the confirmed-orphan signal can be asserted.
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(loggerMock.Object);

            // Enqueued 90 minutes ago — past the 60-minute pending-age window.
            var enqueuedAt = new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(-90);

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

            // The entity confirms this is a genuine orphan (still present, never dispatched). It does NOT remove
            // the entry; the orchestrator removes it via the shared Remove block after the Error update succeeds.
            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.IsConfirmedOrphan),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // The job is transitioned to Error exactly once, carrying the orphaned item's identity.
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.Is<JobStatusUpdaterRequest>(r =>
                    r.Status == SyncStatus.Error && r.SyncJob.Id == jobId && r.SyncJob.RunId == runId),
                It.IsAny<TaskOptions>()), Times.Once());

            // The orphan is confirmed once via the authoritative (non-mutating) check, then removed once via the
            // shared Remove block — but only after the Error status update has succeeded.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.IsConfirmedOrphan),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // The capacity lease acquired for the dispatch attempt is released exactly once — not leaked on the orphan path.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.Is<Guid>(r => r == runId),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // The confirmed-orphan signal (EventId 120077) is emitted exactly once.
            loggerMock.Verify(l => l.Log(
                LogLevel.Warning,
                It.Is<EventId>(e => e.Id == 120077),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once());

            // Removal is forward progress → ContinueAsNew.
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_RemovesWithoutError_WhenDispatchedItemMessageNotFoundAgedPastWindow()
        {
            // Regression guard for the confirmed-orphan gate: an ALREADY-DISPATCHED item whose deferred
            // message is gone and that has aged past MaxPendingAgeMinutes must NOT be transitioned to Error.
            // Age + missing-message is the orphan trigger only for never-dispatched items; once dispatched,
            // the receive activity reports ShouldRemoveFromIndex = AlreadyDispatched = true (the message is
            // gone because it was already dispatched and completed), so the entry is cleanly removed and the
            // orphan-Error branch (guarded by !shouldRemove) is unreachable.
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 305;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            // Capture the orchestrator's logger so the confirmed-orphan signal can be asserted absent.
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(loggerMock.Object);

            // Already dispatched and enqueued 90 minutes ago — well past the 60-minute pending-age window.
            var enqueuedAt = new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(-90);

            context.Setup(x => x.Entities.CallEntityAsync<DeferredPendingItem>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNext),
                    It.IsAny<TakeNextRequest>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(new DeferredPendingItem(sequenceNumber, runId, enqueuedAt, jobId)
                {
                    Dispatched = true,
                    OrchestrationInstanceId = "orch-dispatched"
                });

            // Production contract for an already-dispatched, message-not-found item: ShouldRemoveFromIndex
            // is coupled to AlreadyDispatched and therefore true.
            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: true,
                    ShouldRemoveFromIndex: true,
                    OrchestrationInstanceId: "orch-dispatched",
                    MessageNotFound: true));

            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.Remove),
                    It.Is<long>(s => s == sequenceNumber),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(true);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // The job is NEVER transitioned to Error — even though the message is gone and the entry aged past the window.
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // The confirmed-orphan signal (EventId 120077) is never emitted for a dispatched item.
            loggerMock.Verify(l => l.Log(
                LogLevel.Warning,
                It.Is<EventId>(e => e.Id == 120077),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never());

            // The entry is cleanly removed (ShouldRemoveFromIndex), not tail-requeued.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_DoesNotErrorJob_WhenOrphanGateLosesRaceToConcurrentDrain()
        {
            // Regression guard for the double-drain race (US 16306649): two concurrent drains can take the
            // same index entry across the 60-second in-progress lease boundary under load. The winner
            // dispatches the deferred message and removes the entry; the loser resumes with a STALE,
            // never-dispatched snapshot, finds the message gone (MessageNotFound) and aged past the window,
            // and — absent this guard — would spuriously transition the already-dispatched job to Error.
            // The authoritative IsConfirmedOrphan check returns false (a peer already removed/dispatched the
            // entry), so the loser must NOT error the job. It idempotently REMOVES the stale entry (Remove
            // no-ops if the peer already removed it) so a ghost dispatcher cannot leave a permanent, later-
            // mis-Errored row, and it continues draining.
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 310;

            // Aged 90 minutes (past the 60-minute window) with a never-dispatched local snapshot — exactly
            // the inputs that drive the orphan gate — yet the entry has been claimed by a peer drain.
            var context = CreateNotFoundContext(
                utcNow, lane, sequenceNumber, runId, jobId, ageMinutes: 90,
                out var indexEntityId, out var limiterEntityId);

            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(loggerMock.Object);

            // The authoritative entity check reports the entry is no longer a confirmed orphan: a peer drain
            // already dispatched/removed it. (Overrides the helper's default-true confirmation.)
            context.Setup(x => x.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.IsConfirmedOrphan),
                    It.IsAny<long>(),
                    It.IsAny<CallEntityOptions>()))
                .ReturnsAsync(false);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // The job is NEVER transitioned to Error — the peer drain dispatched it successfully.
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // The confirmed-orphan signal (EventId 120077) is never emitted on the losing path.
            loggerMock.Verify(l => l.Log(
                LogLevel.Warning,
                It.Is<EventId>(e => e.Id == 120077),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never());

            // The superseded-by-peer signal (EventId 120078) is emitted exactly once for observability.
            loggerMock.Verify(l => l.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 120078),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once());

            // The entry is healed, not left for a (possibly ghost) peer: the loser REMOVES it idempotently and
            // never re-tails it. Remove no-ops if the peer already removed it, so this is safe in the genuine
            // two-drain race and prevents a permanent poison row (and a later purge-driven false Error) when the
            // dispatcher crashed before MarkDispatched or lost its activity result to an at-least-once replay.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Once());
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // The capacity lease acquired for the dispatch attempt is still released exactly once (not leaked).
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.Is<Guid>(r => r == runId),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Removing the stale entry is forward progress → the drain continues via ContinueAsNew.
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_DoesNotErrorJob_AndSkipsEntityCheck_WhenPeerOrchestrationExists()
        {
            // Regression guard closing the residual double-drain window (US 16306649): a loser can observe the
            // deferred message as "not found" AFTER the winner completed it but BEFORE the winner's MarkDispatched
            // lands on the single-threaded entity. In that sliver the entity still reads present-and-never-
            // dispatched, so the entity IsConfirmedOrphan check alone could still spuriously Error the job.
            // The receive activity resolves this with a race-free signal: the winner creates the deterministic
            // orchestration BEFORE it completes the Service Bus message (which happens-before the loser's
            // not-found observation), so PeerOrchestrationExists=true authoritatively proves the item was
            // dispatched. The orchestrator must then treat it as superseded WITHOUT consulting the entity at all.
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 311;

            // Aged 90 minutes (past the 60-minute window) with a never-dispatched local snapshot — the exact
            // inputs that drive the orphan gate — but the peer's GraphUpdater orchestration already exists.
            var context = CreateNotFoundContext(
                utcNow, lane, sequenceNumber, runId, jobId, ageMinutes: 90,
                out var indexEntityId, out var limiterEntityId);

            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(loggerMock.Object);

            // The receive activity reports the message gone AND that a peer already created the deterministic
            // orchestration for this exact item. (Overrides the helper's default PeerOrchestrationExists=false.)
            context.Setup(x => x.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    It.IsAny<ReceiveDeferredPendingRequest>(),
                    It.IsAny<TaskOptions>()))
                .ReturnsAsync(new ReceiveDeferredPendingResponse(
                    Dispatched: false,
                    ShouldRemoveFromIndex: false,
                    OrchestrationInstanceId: null,
                    MessageNotFound: true,
                    PeerOrchestrationExists: true));

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // The authoritative instance signal short-circuits the decision: the entity IsConfirmedOrphan check
            // is never consulted, which is precisely what removes the residual pre-MarkDispatched race.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.IsConfirmedOrphan),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // The job is NEVER transitioned to Error — a peer dispatched it successfully.
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // The confirmed-orphan signal (EventId 120077) is never emitted on the losing path.
            loggerMock.Verify(l => l.Log(
                LogLevel.Warning,
                It.Is<EventId>(e => e.Id == 120077),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never());

            // The superseded-by-peer signal (EventId 120078) is emitted exactly once for observability.
            loggerMock.Verify(l => l.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 120078),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once());

            // The entry is healed, not left for a (possibly ghost) peer: the loser REMOVES it idempotently and
            // never re-tails it. The authoritative instance signal proves dispatch happened, so removing the row
            // here is what prevents a permanent poison entry and the delayed false Error that the IsConfirmedOrphan
            // fallback would raise after the peer orchestration is eventually purged.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Once());
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // The capacity lease acquired for the dispatch attempt is still released exactly once (not leaked).
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.Is<Guid>(r => r == runId),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Removing the stale entry is forward progress → the drain continues via ContinueAsNew.
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_KeepsOrphanForRetry_WhenStatusUpdateThrowsAfterConfirm()
        {
            // Regression guard (US 16306649): the confirmed-orphan path must record the Error status BEFORE it
            // removes the index entry. If the JobStatusUpdater activity throws, the entry must remain in the
            // index (re-tailed via ReleaseInProgress) so a later drain retries it — the job must never be
            // silently dropped without a status update, and the capacity lease must not be double-released.
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 315;

            // Aged 90 minutes past the window with a never-dispatched snapshot and a missing message: the
            // helper's default IsConfirmedOrphan → true drives the orphan-Error path.
            var context = CreateNotFoundContext(
                utcNow, lane, sequenceNumber, runId, jobId, ageMinutes: 90,
                out var indexEntityId, out var limiterEntityId);

            // The status update fails (e.g., a transient store error) after the orphan is confirmed.
            context.Setup(x => x.CallActivityAsync(
                    nameof(JobStatusUpdaterFunction),
                    It.IsAny<object>(),
                    It.IsAny<TaskOptions>()))
                .ThrowsAsync(new InvalidOperationException("status update failed"));

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);

            // The orchestrator re-throws so Durable Functions can retry the orchestration.
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => orchestrator.RunAsync(context.Object));

            // The orphan was confirmed and the Error update attempted exactly once.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.IsConfirmedOrphan),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Once());

            // The entry is NOT removed — the failed status update must leave it in the index for retry.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Remove),
                It.IsAny<long>(),
                It.IsAny<CallEntityOptions>()), Times.Never());

            // The surviving entry is re-tailed once by the catch block so the next TakeNext can retry it.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // The capacity lease is released exactly once (on the not-dispatched path); the catch block must
            // NOT release it again now that the lease flag is cleared after the first release.
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.Is<Guid>(r => r == runId),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // A failed orphan-Error is not forward progress → no ContinueAsNew.
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_RetriesWithoutError_WhenMessageNotFoundWithinWindow()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 400;

            var context = CreateContext(utcNow, lane, out var indexEntityId, out var limiterEntityId);

            // Enqueued 30 minutes ago — within the 60-minute pending-age window.
            var enqueuedAt = new DateTimeOffset(utcNow, TimeSpan.Zero).AddMinutes(-30);

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

            // A within-window not-found entry is never transitioned to Error.
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // Should keep in index (release in place, not remove) so it is retried.
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
        public async Task RunAsync_HonorsConfiguredMaxPendingAge_ErrorsOrphanAgedPastConfiguredWindow()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 600;

            // Configured window is 30 minutes and the entry is 45 minutes old: orphaned only
            // because the configured value is honored — the default 60-minute window would retry.
            var context = CreateNotFoundContext(
                utcNow, lane, sequenceNumber, runId, jobId, ageMinutes: 45,
                out var indexEntityId, out _);

            var orchestrator = new DeferredPendingDrainOrchestrator(SettingsWithMaxPendingAge(30));
            await orchestrator.RunAsync(context.Object);

            // The job is transitioned to Error and the entry is then removed via the shared Remove block.
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.Is<JobStatusUpdaterRequest>(r =>
                    r.Status == SyncStatus.Error && r.SyncJob.Id == jobId && r.SyncJob.RunId == runId),
                It.IsAny<TaskOptions>()), Times.Once());

            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.IsConfirmedOrphan),
                It.Is<long>(s => s == sequenceNumber),
                It.IsAny<CallEntityOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task RunAsync_RetriesWithoutError_WhenMessageNotFoundExactlyAtWindowBoundary()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 605;

            // Age equals the pending-age window exactly (60 == 60). The orphan gate is strictly
            // greater-than (age > MaxPendingAgeMinutes), so an entry sitting precisely at the
            // boundary is NOT yet a confirmed orphan: it must be retried, not errored. This guards
            // against a future regression that relaxes the comparison from `>` to `>=`.
            var context = CreateNotFoundContext(
                utcNow, lane, sequenceNumber, runId, jobId, ageMinutes: 60,
                out var indexEntityId, out var limiterEntityId);

            var orchestrator = new DeferredPendingDrainOrchestrator(DefaultSettings);
            await orchestrator.RunAsync(context.Object);

            // At the exact boundary: never errored.
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // Released in place for retry, not removed.
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

            // Lease released (no work dispatched).
            context.Verify(x => x.Entities.CallEntityAsync<bool>(
                limiterEntityId,
                nameof(RunLimiter.Release),
                It.Is<Guid>(r => r == runId),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // No forward progress → no continue.
            context.Verify(x => x.ContinueAsNew(It.IsAny<object>(), It.IsAny<bool>()), Times.Never());
        }

        [TestMethod]
        public async Task RunAsync_FallsBackToDefaultWindow_WhenConfiguredMaxPendingAgeNonPositive()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var lane = "small";
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            const long sequenceNumber = 610;

            // Configured value is non-positive, so the default 60-minute window applies. At 30
            // minutes old the entry is within that default window and must be retried, not errored.
            var context = CreateNotFoundContext(
                utcNow, lane, sequenceNumber, runId, jobId, ageMinutes: 30,
                out var indexEntityId, out _);

            var orchestrator = new DeferredPendingDrainOrchestrator(SettingsWithMaxPendingAge(0));
            await orchestrator.RunAsync(context.Object);

            // Within the fallback window: never errored.
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // Released in place for retry, not removed.
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
        }

        [TestMethod]
        public async Task RunAsync_EvaluatesEachLaneAgainstItsOwnThreshold()
        {
            var utcNow = new DateTime(2025, 12, 17, 12, 0, 0, DateTimeKind.Utc);
            var smallRunId = Guid.NewGuid();
            var smallJobId = Guid.NewGuid();
            var largeRunId = Guid.NewGuid();
            var largeJobId = Guid.NewGuid();
            const long smallSeq = 620;
            const long largeSeq = 720;

            // Same 60-minute age, different per-lane thresholds: the small lane (30) treats it as an
            // orphan while the large lane (120) keeps retrying — proving each lane uses its own value.
            var smallContext = CreateNotFoundContext(
                utcNow, "small", smallSeq, smallRunId, smallJobId, ageMinutes: 60,
                out var smallIndexEntityId, out _);
            var largeContext = CreateNotFoundContext(
                utcNow, "large", largeSeq, largeRunId, largeJobId, ageMinutes: 60,
                out var largeIndexEntityId, out _);

            await new DeferredPendingDrainOrchestrator(SettingsWithMaxPendingAge(30)).RunAsync(smallContext.Object);
            await new DeferredPendingDrainOrchestrator(SettingsWithMaxPendingAge(120)).RunAsync(largeContext.Object);

            // Small lane: aged past its 30-minute window → Error, then removal via the shared Remove block.
            smallContext.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.Is<JobStatusUpdaterRequest>(r => r.Status == SyncStatus.Error && r.SyncJob.Id == smallJobId),
                It.IsAny<TaskOptions>()), Times.Once());
            smallContext.Verify(x => x.Entities.CallEntityAsync<bool>(
                smallIndexEntityId,
                nameof(DeferredPendingIndexEntity.IsConfirmedOrphan),
                It.Is<long>(s => s == smallSeq),
                It.IsAny<CallEntityOptions>()), Times.Once());

            // Large lane: within its 120-minute window → no Error, released for retry.
            largeContext.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());
            largeContext.Verify(x => x.Entities.CallEntityAsync<bool>(
                largeIndexEntityId,
                nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                It.Is<long>(s => s == largeSeq),
                It.IsAny<CallEntityOptions>()), Times.Once());
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

            // Enqueued 5 minutes ago — well within the 60-minute pending-age window.
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

            // A within-window not-found entry is never transitioned to Error.
            context.Verify(x => x.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                It.IsAny<object>(),
                It.IsAny<TaskOptions>()), Times.Never());

            // Should keep in index (within the pending-age window) so it is retried.
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
