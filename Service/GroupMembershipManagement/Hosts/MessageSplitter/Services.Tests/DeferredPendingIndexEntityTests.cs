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
            entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));

            // Item is in-progress, second take should return null
            var second = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));
            Assert.IsNull(second);

            // Release the in-progress marker
            entity.ReleaseInProgress(100);

            // Item should now be available
            var third = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));
            Assert.IsNotNull(third);
            Assert.AreEqual(100, third.SequenceNumber);
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
        public void PruneOlderThanMinutes_CapacityDeniedItems_NeverPruned()
        {
            var entity = new DeferredPendingIndexEntity();
            var t0 = DateTimeOffset.UtcNow;

            // Item 100: 600 min old, capacity-denied — should NEVER be pruned
            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), t0.AddMinutes(-600), Guid.NewGuid()));
            entity.TakeNextBatch(new TakeNextBatchRequest(t0.AddMinutes(-599), MaxItems: 1, InProgressSeconds: 60));
            entity.MarkCapacityDeniedAndRelease(new MarkCapacityDeniedRequest(100, t0.AddMinutes(-599)));

            // Item 200: 600 min old, no capacity denial — should be pruned
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), t0.AddMinutes(-600), Guid.NewGuid()));

            var pruned = entity.PruneOlderThanMinutes(new PruneOlderThanMinutesRequest(t0, 120));

            Assert.AreEqual(1, pruned.Count);
            Assert.AreEqual(200, pruned[0].SequenceNumber);
            Assert.AreEqual(1, entity.GetState().Items.Count);
            Assert.AreEqual(100, entity.GetState().Items[0].SequenceNumber);
        }

        [TestMethod]
        public void PruneOlderThanMinutes_DispatchedItems_NeverPruned()
        {
            var entity = new DeferredPendingIndexEntity();
            var t0 = DateTimeOffset.UtcNow;

            // Item 100: 600 min old, dispatched — should NEVER be pruned
            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), t0.AddMinutes(-600), Guid.NewGuid()));
            entity.MarkDispatched(new MarkDeferredPendingDispatchedRequest(100, "instance-abc"));

            // Item 200: 600 min old, not dispatched, not capacity-denied — should be pruned
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), t0.AddMinutes(-600), Guid.NewGuid()));

            // Item 300: 10 min old, not dispatched — should NOT be pruned (too young)
            entity.Add(new AddDeferredPendingRequest(300, Guid.NewGuid(), t0.AddMinutes(-10), Guid.NewGuid()));

            var pruned = entity.PruneOlderThanMinutes(new PruneOlderThanMinutesRequest(t0, 120));

            Assert.AreEqual(1, pruned.Count);
            Assert.AreEqual(200, pruned[0].SequenceNumber);
            Assert.AreEqual(2, entity.GetState().Items.Count);
            Assert.IsTrue(entity.GetState().Items.Any(i => i.SequenceNumber == 100));
            Assert.IsTrue(entity.GetState().Items.Any(i => i.SequenceNumber == 300));
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
        [TestMethod]
        public void TakeNextBatch_ReturnsFifoOrder()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            // Add items in specific order
            entity.Add(new AddDeferredPendingRequest(300, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), now, Guid.NewGuid()));

            var batch = entity.TakeNextBatch(new TakeNextBatchRequest(now, MaxItems: 3, InProgressSeconds: 60));

            // Items should come back in insertion order (FIFO), not sorted by sequence number
            Assert.AreEqual(3, batch.Count);
            Assert.AreEqual(300, batch[0].SequenceNumber);
            Assert.AreEqual(100, batch[1].SequenceNumber);
            Assert.AreEqual(200, batch[2].SequenceNumber);
        }

        [TestMethod]
        public void ReleaseInProgress_ReturnsFalse_WhenItemNotFound()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));

            var result = entity.ReleaseInProgress(999);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void MarkCapacityDeniedAndRelease_SetsTimestampAndClearsInProgress()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));

            // Take the item to mark it in-progress
            entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));

            // Item should be in-progress
            var second = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));
            Assert.IsNull(second);

            // Mark capacity denied
            var deniedAt = now.AddSeconds(5);
            var result = entity.MarkCapacityDeniedAndRelease(new MarkCapacityDeniedRequest(100, deniedAt));

            Assert.IsTrue(result);

            // Item should be at the tail (only item, so still index 0)
            var state = entity.GetState();
            Assert.AreEqual(deniedAt, state.Items[0].LastCapacityDeniedAtUtc);
            Assert.IsNull(state.Items[0].InProgressUntilUtc);

            // Item should be available again
            var third = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));
            Assert.IsNotNull(third);
            Assert.AreEqual(100, third.SequenceNumber);
        }

        [TestMethod]
        public void MarkCapacityDeniedAndRelease_ReturnsFalse_WhenItemNotFound()
        {
            var entity = new DeferredPendingIndexEntity();

            var result = entity.MarkCapacityDeniedAndRelease(new MarkCapacityDeniedRequest(999, DateTimeOffset.UtcNow));

            Assert.IsFalse(result);
            Assert.AreEqual(0, entity.GetState().Items.Count);
        }

        // ── TakeNext tests ──

        [TestMethod]
        public void TakeNext_ReturnsSingleItem()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), now, Guid.NewGuid()));

            var item = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));

            Assert.IsNotNull(item);
            Assert.AreEqual(100, item.SequenceNumber);
            Assert.IsNotNull(item.InProgressUntilUtc);
        }

        [TestMethod]
        public void TakeNext_ReturnsNull_WhenEmpty()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            var item = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));

            Assert.IsNull(item);
        }

        [TestMethod]
        public void TakeNext_SkipsInProgressItems()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), now, Guid.NewGuid()));

            // Take first item
            var first = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));
            Assert.AreEqual(100, first.SequenceNumber);

            // Second take should skip item 100 (in-progress) and pick 200
            var second = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));
            Assert.AreEqual(200, second.SequenceNumber);

            // Third take should return null (both in-progress)
            var third = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));
            Assert.IsNull(third);
        }

        [TestMethod]
        public void TakeNext_ClearsExpiredInProgressMarkers()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));

            // Lock it with a 60s timeout
            entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));

            // Should be locked
            Assert.IsNull(entity.TakeNext(new TakeNextRequest(now.AddSeconds(30), InProgressSeconds: 60)));

            // After expiry, should be available again
            var after = entity.TakeNext(new TakeNextRequest(now.AddSeconds(61), InProgressSeconds: 60));
            Assert.IsNotNull(after);
            Assert.AreEqual(100, after.SequenceNumber);
        }

        // ── Move-to-tail tests ──

        [TestMethod]
        public void ReleaseInProgress_MovesItemToTail()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(300, Guid.NewGuid(), now, Guid.NewGuid()));

            // Take item 100
            entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));

            // Release it — should move to tail
            entity.ReleaseInProgress(100);

            // Next TakeNext should pick item 200 (not 100, which is now at tail)
            var next = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));
            Assert.AreEqual(200, next.SequenceNumber);

            // Verify order: 200, 300, 100
            var state = entity.GetState();
            Assert.AreEqual(200, state.Items[0].SequenceNumber);
            Assert.AreEqual(300, state.Items[1].SequenceNumber);
            Assert.AreEqual(100, state.Items[2].SequenceNumber);
        }

        [TestMethod]
        public void MarkCapacityDeniedAndRelease_MovesItemToTail()
        {
            var entity = new DeferredPendingIndexEntity();
            var now = DateTimeOffset.UtcNow;

            entity.Add(new AddDeferredPendingRequest(100, Guid.NewGuid(), now, Guid.NewGuid()));
            entity.Add(new AddDeferredPendingRequest(200, Guid.NewGuid(), now, Guid.NewGuid()));

            // Take item 100
            entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));

            // Mark capacity denied — should move to tail
            entity.MarkCapacityDeniedAndRelease(new MarkCapacityDeniedRequest(100, now));

            // Next TakeNext should pick item 200 (100 is at tail)
            var next = entity.TakeNext(new TakeNextRequest(now, InProgressSeconds: 60));
            Assert.AreEqual(200, next.SequenceNumber);

            var state = entity.GetState();
            Assert.AreEqual(200, state.Items[0].SequenceNumber);
            Assert.AreEqual(100, state.Items[1].SequenceNumber);
        }
    }
}
