// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class DeferredPendingIndexEntity : TaskEntity<DeferredPendingIndexState>
    {
        [Function(nameof(DeferredPendingIndexEntity))]
        public static Task Run([EntityTrigger] TaskEntityDispatcher dispatcher)
        {
            return dispatcher.DispatchAsync<DeferredPendingIndexEntity>();
        }

        public void Add(AddDeferredPendingRequest request)
        {
            State ??= new DeferredPendingIndexState();

            if (State.Items.Any(i => i.SequenceNumber == request.SequenceNumber))
            {
                return;
            }

            State.Items.Add(new DeferredPendingItem(request.SequenceNumber, request.RunId, request.UtcNow, request.JobId));
        }

        public bool TryAcquireDrainLock(TryAcquireDrainLockRequest request)
        {
            State ??= new DeferredPendingIndexState();

            if (State.DrainLockUntilUtc.HasValue && State.DrainLockUntilUtc.Value > request.UtcNow)
            {
                return false;
            }

            State.DrainLockUntilUtc = request.UtcNow.AddSeconds(request.LockSeconds);
            return true;
        }

        public void ReleaseDrainLock()
        {
            State ??= new DeferredPendingIndexState();
            State.DrainLockUntilUtc = null;
        }

        /// <summary>
        /// Takes a single available item for processing. Returns null if no items are available.
        /// This replaces TakeNextBatch to enable single-item drain processing, which eliminates
        /// the class of bugs caused by batch-take with early termination (lock leaks, capacity
        /// slot waste, cascading sweep prunes).
        /// </summary>
        public DeferredPendingItem TakeNext(TakeNextRequest request)
        {
            State ??= new DeferredPendingIndexState();

            if (State.Items.Count == 0)
            {
                return null;
            }

            // Clear any expired in-progress markers.
            foreach (var item in State.Items)
            {
                if (item.InProgressUntilUtc.HasValue && item.InProgressUntilUtc.Value <= request.UtcNow)
                {
                    item.InProgressUntilUtc = null;
                }
            }

            var nextItem = State.Items.FirstOrDefault(i => !i.InProgressUntilUtc.HasValue);
            if (nextItem == null)
            {
                return null;
            }

            nextItem.InProgressUntilUtc = request.UtcNow.AddSeconds(request.InProgressSeconds);
            return nextItem;
        }

        public List<DeferredPendingItem> TakeNextBatch(TakeNextBatchRequest request)
        {
            State ??= new DeferredPendingIndexState();

            if (State.Items.Count == 0)
            {
                return new List<DeferredPendingItem>();
            }

            // Clear any expired in-progress markers.
            foreach (var item in State.Items)
            {
                if (item.InProgressUntilUtc.HasValue && item.InProgressUntilUtc.Value <= request.UtcNow)
                {
                    item.InProgressUntilUtc = null;
                }
            }

            var picked = new List<DeferredPendingItem>(capacity: Math.Min(request.MaxItems, State.Items.Count));
            foreach (var item in State.Items)
            {
                if (picked.Count >= request.MaxItems)
                {
                    break;
                }

                if (item.InProgressUntilUtc.HasValue)
                {
                    continue;
                }

                item.InProgressUntilUtc = request.UtcNow.AddSeconds(request.InProgressSeconds);
                picked.Add(item);
            }

            return picked;
        }

        public bool Remove(long sequenceNumber)
        {
            State ??= new DeferredPendingIndexState();

            var index = State.Items.FindIndex(i => i.SequenceNumber == sequenceNumber);
            if (index < 0)
            {
                return false;
            }

            State.Items.RemoveAt(index);
            return true;
        }

        public bool ReleaseInProgress(long sequenceNumber)
        {
            State ??= new DeferredPendingIndexState();

            var index = State.Items.FindIndex(i => i.SequenceNumber == sequenceNumber);
            if (index < 0)
            {
                return false;
            }

            var item = State.Items[index];
            item.InProgressUntilUtc = null;

            // Move to tail so TakeNext picks a different item next time,
            // preventing head-of-line blocking in single-item drain mode.
            State.Items.RemoveAt(index);
            State.Items.Add(item);
            return true;
        }

        /// <summary>
        /// Marks the item as having been denied capacity at the specified time and releases its in-progress marker.
        /// This timestamp is used to suppress repeated "no capacity" log messages.
        /// </summary>
        public bool MarkCapacityDeniedAndRelease(MarkCapacityDeniedRequest request)
        {
            State ??= new DeferredPendingIndexState();

            var index = State.Items.FindIndex(i => i.SequenceNumber == request.SequenceNumber);
            if (index < 0)
            {
                return false;
            }

            var item = State.Items[index];
            item.LastCapacityDeniedAtUtc = request.DeniedAtUtc;
            item.InProgressUntilUtc = null;

            // Move to tail so next drain picks a different item,
            // allowing items from different RunIds to get capacity.
            State.Items.RemoveAt(index);
            State.Items.Add(item);
            return true;
        }

        public bool MarkDispatched(MarkDeferredPendingDispatchedRequest request)
        {
            State ??= new DeferredPendingIndexState();

            var item = State.Items.FirstOrDefault(i => i.SequenceNumber == request.SequenceNumber);
            if (item == null)
            {
                return false;
            }

            item.Dispatched = true;
            if (!string.IsNullOrWhiteSpace(request.OrchestrationInstanceId))
            {
                item.OrchestrationInstanceId ??= request.OrchestrationInstanceId;
            }

            return true;
        }

        /// <summary>
        /// Authoritatively reports whether the entry for <paramref name="sequenceNumber"/> is a genuine orphan.
        /// An entry qualifies as an orphan only when it is still present in the index AND was never dispatched.
        /// Under load a peer drain can re-take an entry whose in-progress lease expired, dispatch it, and remove
        /// it; the losing drain is then left holding a stale snapshot and a benign "message not found".
        /// Re-checking on this single-threaded entity — which processes the call atomically with respect to the
        /// peer's MarkDispatched/Remove — prevents the losing drain from failing a job the peer already
        /// dispatched successfully.
        /// </summary>
        /// <remarks>
        /// This method is a pure query: it does NOT mutate the index. The caller removes the entry only after it
        /// has durably recorded the job's Error status, so a failed status update leaves the entry in the index
        /// to be retried instead of silently dropping the job.
        /// </remarks>
        /// <param name="sequenceNumber">The sequence number of the entry to confirm.</param>
        /// <returns>
        /// <c>true</c> only when the entry is still present and has not been dispatched (a confirmed orphan).
        /// <c>false</c> when the entry is absent (a peer already removed it) or already dispatched (a peer
        /// already dispatched it), in which case it is NOT an orphan.
        /// </returns>
        public bool IsConfirmedOrphan(long sequenceNumber)
        {
            State ??= new DeferredPendingIndexState();

            var index = State.Items.FindIndex(i => i.SequenceNumber == sequenceNumber);
            if (index < 0)
            {
                // A peer drain already removed (dispatched) this entry — not an orphan.
                return false;
            }

            // A still-present, never-dispatched entry is a genuine orphan. A dispatched entry means a peer
            // drain already dispatched it while we held a stale snapshot — not an orphan.
            return !State.Items[index].Dispatched;
        }

        public DeferredPendingIndexState GetState()
        {
            State ??= new DeferredPendingIndexState();
            return State;
        }
    }
}
