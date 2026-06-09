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

        public DeferredPendingIndexState GetState()
        {
            State ??= new DeferredPendingIndexState();
            return State;
        }

        public List<DeferredPendingItem> PruneOlderThanMinutes(PruneOlderThanMinutesRequest request)
        {
            State ??= new DeferredPendingIndexState();

            if (State.Items.Count == 0)
            {
                return new List<DeferredPendingItem>();
            }

            var cutoff = request.UtcNow.AddMinutes(-request.MaxAgeMinutes);

            // Never prune items the system is actively managing:
            // - Dispatched items are being processed by GU (may take hours for large groups).
            // - Capacity-denied items are waiting for a slot — the drain will dispatch them
            //   when capacity frees up, regardless of how long they've waited.
            // Only prune truly orphaned items: not dispatched, never capacity-denied, and older
            // than the threshold. These are entries that were indexed but never picked up by
            // the drain, indicating a possible enqueue/index race or messaging failure.
            var pruned = State.Items
                .Where(i => !i.Dispatched
                             && !i.LastCapacityDeniedAtUtc.HasValue
                             && i.EnqueuedAtUtc < cutoff)
                .ToList();

            var prunedSeqs = new HashSet<long>(pruned.Select(i => i.SequenceNumber));
            State.Items = State.Items.Where(i => !prunedSeqs.Contains(i.SequenceNumber)).ToList();
            return pruned;
        }
    }
}
