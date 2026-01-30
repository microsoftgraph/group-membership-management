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

            var item = State.Items.FirstOrDefault(i => i.SequenceNumber == sequenceNumber);
            if (item == null)
            {
                return false;
            }

            item.InProgressUntilUtc = null;
            return true;
        }

        /// <summary>
        /// Marks the item as having been denied capacity at the specified time and releases its in-progress marker.
        /// This timestamp is used to suppress repeated "no capacity" log messages.
        /// </summary>
        public bool MarkCapacityDeniedAndRelease(MarkCapacityDeniedRequest request)
        {
            State ??= new DeferredPendingIndexState();

            var item = State.Items.FirstOrDefault(i => i.SequenceNumber == request.SequenceNumber);
            if (item == null)
            {
                return false;
            }

            item.LastCapacityDeniedAtUtc = request.DeniedAtUtc;
            item.InProgressUntilUtc = null;
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

        public List<DeferredPendingItem> PruneOlderThanMinutes((DateTimeOffset UtcNow, int MaxAgeMinutes) request)
        {
            State ??= new DeferredPendingIndexState();

            if (State.Items.Count == 0)
            {
                return new List<DeferredPendingItem>();
            }

            var cutoff = request.UtcNow.AddMinutes(-request.MaxAgeMinutes);
            var pruned = State.Items.Where(i => i.EnqueuedAtUtc < cutoff).ToList();
            State.Items = State.Items.Where(i => i.EnqueuedAtUtc >= cutoff).ToList();
            return pruned;
        }
    }
}
