// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Services.Tests
{
    [TestClass]
    public class DeferredPendingIndexEntityTests
    {
        [TestMethod]
        public void Add_AddsNewItem()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;
            var runId = Guid.NewGuid();
            var jobId = Guid.NewGuid();

            entity.Add(new AddDeferredPendingRequest(123, runId, now, jobId));

            var state = entity.GetState();
            Assert.AreEqual(1, state.Items.Count);
            Assert.AreEqual(123, state.Items[0].SequenceNumber);
            Assert.AreEqual(runId, state.Items[0].RunId);
            Assert.AreEqual(jobId, state.Items[0].JobId);
        }

        [TestMethod]
        public void Add_IgnoresDuplicateSequenceNumber()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(123, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(123, Guid.NewGuid(), now, Guid.NewGuid()));

            var state = entity.GetState();
            Assert.AreEqual(1, state.Items.Count);
        }

        [TestMethod]
        public void Remove_RemovesExistingItem()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(123, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(456, Guid.NewGuid(), now, Guid.NewGuid()));

            var result = entity.Remove(123);

            Assert.IsTrue(result);
            var state = entity.GetState();
            Assert.AreEqual(1, state.Items.Count);
            Assert.AreEqual(456, state.Items[0].SequenceNumber);
        }

        [TestMethod]
        public void Remove_ReturnsFalse_WhenItemNotFound()
        {
            var entity = new DeferredPendingIndexEntity();

            var result = entity.Remove(999);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TakeNextBatch_ReturnsItemsNotInProgress()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(300, Guid.NewGuid(), now, Guid.NewGuid()));

            var batch = entity.TakeNextBatch(new TakeNextBatchRequest(now, MaxItems: 2, InProgressSeconds: 60));

            Assert.AreEqual(2, batch.Count);
            Assert.AreEqual(100, batch[0].SequenceNumber);
            Assert.AreEqual(200, batch[1].SequenceNumber);

            // Items should now be marked in-progress
            var batch2 = entity.TakeNextBatch(new TakeNextBatchRequest(now, MaxItems: 2, InProgressSeconds: 60));
            Assert.AreEqual(1, batch2.Count);
            Assert.AreEqual(300, batch2[0].SequenceNumber);
        }

        [TestMethod]
        public void TakeNextBatch_ClearsExpiredInProgressMarkers()
        {
            var entity = new DeferredPendingIndexEntity();
            var t0 = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), t0, Guid.NewGuid()));

            // Take the item (marks in-progress for 60 seconds)
            entity.TakeNextBatch(new TakeNextBatchRequest(t0, MaxItems: 1, InProgressSeconds: 60));

            // Advance time past the in-progress timeout
            var t1 = t0.AddSeconds(120);
            var batch = entity.TakeNextBatch(new TakeNextBatchRequest(t1, MaxItems: 1, InProgressSeconds: 60));

            // Item should be available again
            Assert.AreEqual(1, batch.Count);
            Assert.AreEqual(100, batch[0].SequenceNumber);
        }

        [TestMethod]
        public void ReleaseInProgress_ClearsInProgressMarker()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.TakeNextBatch(new TakeNextBatchRequest(now, MaxItems: 1, InProgressSeconds: 60));

            // Item is in-progress, second batch should be empty
            var batch1 = entity.TakeNextBatch(new TakeNextBatchRequest(now, MaxItems: 1, InProgressSeconds: 60));
            Assert.AreEqual(0, batch1.Count);

            // Release the in-progress marker
            entity.ReleaseInProgress(100);

            // Item should now be available
            var batch2 = entity.TakeNextBatch(new TakeNextBatchRequest(now, MaxItems: 1, InProgressSeconds: 60));
            Assert.AreEqual(1, batch2.Count);
        }

        [TestMethod]
        public void PruneOlderThanMinutes_RemovesOldItems()
        {
            var entity = new DeferredPendingIndexEntity();
            var t0 = DateTimeOffset.UtcNow;

            // Add items at different times
            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), t0.AddMinutes(-150), Guid.NewGuid())); // 150 min old
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), t0.AddMinutes(-130), Guid.NewGuid())); // 130 min old
            entity.Add(new AddDeferredPendingRequest(300, Guid.NewGuid(), t0.AddMinutes(-60), Guid.NewGuid()));  // 60 min old
            entity.Add(new AddDeferredPendingRequest(400, Guid.NewGuid(), t0, Guid.NewGuid()));                   // Just added

            // Prune items older than 120 minutes
            var pruned = entity.PruneOlderThanMinutes(new PruneOlderThanMinutesRequest(t0, 120));

            Assert.AreEqual(2, pruned.Count); // Should remove items 100 and 200
            Assert.IsTrue(pruned.Any(i => i.SequenceNumber == 100));
            Assert.IsTrue(pruned.Any(i => i.SequenceNumber == 200));

            var state = entity.GetState();
            Assert.AreEqual(2, state.Items.Count);
            Assert.IsTrue(state.Items.Any(i => i.SequenceNumber == 300));
            Assert.IsTrue(state.Items.Any(i => i.SequenceNumber == 400));
        }

        [TestMethod]
        public void PruneOlderThanMinutes_ReturnsEmptyList_WhenNoOldItems()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), now, Guid.NewGuid()));

            var pruned = entity.PruneOlderThanMinutes(new PruneOlderThanMinutesRequest(now, 120));

            Assert.AreEqual(0, pruned.Count);
            Assert.AreEqual(2, entity.GetState().Items.Count);
        }

        [TestMethod]
        public void TryAcquireDrainLock_AcquiresLock()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            var result = entity.TryAcquireDrainLock(new TryAcquireDrainLockRequest(now, 60));

            Assert.IsTrue(result);
            Assert.IsNotNull(entity.GetState().DrainLockUntilUtc);
        }

        [TestMethod]
        public void TryAcquireDrainLock_FailsWhenLockHeld()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.TryAcquireDrainLock(new TryAcquireDrainLockRequest(now, 60));
            var result = entity.TryAcquireDrainLock(new TryAcquireDrainLockRequest(now, 60));

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryAcquireDrainLock_SucceedsAfterLockExpires()
        {
            var entity = new DeferredPendingIndexEntity();
            var t0 = DateTimeOffset.UtcNow;

            entity.TryAcquireDrainLock(new TryAcquireDrainLockRequest(t0, 60));

            // Advance time past lock expiry
            var t1 = t0.AddSeconds(120);
            var result = entity.TryAcquireDrainLock(new TryAcquireDrainLockRequest(t1, 60));

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void MarkDispatched_SetsDispatchedFlag()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));

            var result = entity.MarkDispatched(new MarkDeferredPendingDispatchedRequest(100, "instance-123"));

            Assert.IsTrue(result);
            var state = entity.GetState();
            Assert.IsTrue(state.Items[0].Dispatched);
            Assert.AreEqual("instance-123", state.Items[0].OrchestrationInstanceId);
        }
    }
}
