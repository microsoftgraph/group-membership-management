// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class DeferredPendingDrainOrchestrator
    {
        private readonly RunLimiterSettings _runLimiterSettings;

        public DeferredPendingDrainOrchestrator(RunLimiterSettings runLimiterSettings)
        {
            _runLimiterSettings = runLimiterSettings ?? throw new ArgumentNullException(nameof(runLimiterSettings));
        }

        [Function(nameof(DeferredPendingDrainOrchestrator))]
        public async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var input = context.GetInput<DeferredPendingDrainRequest>();
            var lane = (input?.LaneSize ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lane))
            {
                // Nothing to do.
                return;
            }

            var logger = context.CreateReplaySafeLogger("MessageSplitter.DeferredPendingDrainOrchestrator");

            logger.DrainStarted(lane);

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);

            var utcNow = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

            // Acquire a short-lived drain lock to reduce redundant drains.
            var lockAcquired = false;
            lockAcquired = await context.Entities.CallEntityAsync<bool>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.TryAcquireDrainLock),
                new TryAcquireDrainLockRequest(utcNow, 60));

            if (!lockAcquired)
            {
                logger.DrainLockNotAcquired(lane);
                return;
            }

            try
            {
                var maxItems = GetMaxDrainBatch(lane);

                var processed = 0;
                var removed = 0;
                var staleRemoved = 0;
                var newlyDispatched = 0;
                var messageNotFound = 0;
                var capacityDenied = 0;

                List<DeferredPendingItem> batch;
                batch = await context.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.TakeNextBatch),
                    new TakeNextBatchRequest(utcNow, maxItems, 120));

                if (batch == null || batch.Count == 0)
                {
                    logger.DrainNoItems(lane);
                    return;
                }

                var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);

                foreach (var item in batch)
                {
                    processed++;
                    var leaseAcquiredForDispatch = false;
                    if (!item.Dispatched)
                    {
                        AcquireLeaseResponse lease;
                        lease = await context.Entities.CallEntityAsync<AcquireLeaseResponse>(
                            limiterEntityId,
                            nameof(RunLimiter.Acquire),
                            new AcquireLeaseRequest(item.RunId, _runLimiterSettings.MaxInFlightMessages, _runLimiterSettings.LeaseTimeoutMinutes, utcNow));

                        if (!lease.Acquired)
                        {
                            capacityDenied++;

                            // Only log if this item wasn't denied capacity recently (within 60 seconds)
                            // to reduce log noise when the same item is repeatedly attempted
                            var shouldLog = !item.LastCapacityDeniedAtUtc.HasValue ||
                                            (utcNow - item.LastCapacityDeniedAtUtc.Value).TotalSeconds > 60;

                            if (shouldLog)
                            {
                                logger.DrainNoCapacity(lane, lease.InFlightCount, item.RunId, item.SequenceNumber);
                            }

                            // Mark capacity denied and release in-progress marker
                            await context.Entities.CallEntityAsync<bool>(
                                indexEntityId,
                                nameof(DeferredPendingIndexEntity.MarkCapacityDeniedAndRelease),
                                new MarkCapacityDeniedRequest(item.SequenceNumber, utcNow));

                            return;
                        }

                        leaseAcquiredForDispatch = true;
                    }

                    ReceiveDeferredPendingResponse received;
                    try
                    {
                        received = await context.CallActivityAsync<ReceiveDeferredPendingResponse>(
                            nameof(ReceiveDeferredPendingFunction),
                            new ReceiveDeferredPendingRequest(item.SequenceNumber, item.RunId, item.Dispatched, item.OrchestrationInstanceId));
                    }
                    catch (Exception ex)
                    {
                        logger.DrainReceiveFailed(ex, lane, item.RunId, item.SequenceNumber);
                        if (leaseAcquiredForDispatch)
                        {
                            // No work was dispatched: release the lease.
                            await context.Entities.CallEntityAsync<bool>(limiterEntityId, nameof(RunLimiter.Release), item.RunId);
                        }

                        // Activity failed: release in-progress marker and let the deferred message unlock naturally.
                        await context.Entities.CallEntityAsync<bool>(
                            indexEntityId,
                            nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                            item.SequenceNumber);

                        throw;
                    }

                    if (received.MessageNotFound)
                    {
                        messageNotFound++;
                    }

                    if (received.Dispatched && !item.Dispatched)
                    {
                        newlyDispatched++;
                        await context.Entities.CallEntityAsync<bool>(
                            indexEntityId,
                            nameof(DeferredPendingIndexEntity.MarkDispatched),
                            new MarkDeferredPendingDispatchedRequest(item.SequenceNumber, received.OrchestrationInstanceId));
                    }

                    // If no work was dispatched, release the lease (including MessageNotFound).
                    if (!received.Dispatched && leaseAcquiredForDispatch)
                    {
                        await context.Entities.CallEntityAsync<bool>(limiterEntityId, nameof(RunLimiter.Release), item.RunId);
                    }

                    var shouldRemove = received.ShouldRemoveFromIndex;

                    // Stale entry detection: if the message is not found in Service Bus and
                    // the item was enqueued more than 5 minutes ago, the message is permanently
                    // gone — not a transient race between enqueue and defer (which takes milliseconds).
                    if (!shouldRemove && received.MessageNotFound
                        && (utcNow - item.EnqueuedAtUtc).TotalMinutes > 5)
                    {
                        shouldRemove = true;
                        staleRemoved++;
                        logger.DrainRemovingStaleEntry(
                            $"{(utcNow - item.EnqueuedAtUtc).TotalMinutes:F1}",
                            item.SequenceNumber,
                            item.JobId,
                            lane);
                    }

                    if (shouldRemove)
                    {
                        removed++;
                        await context.Entities.CallEntityAsync<bool>(
                            indexEntityId,
                            nameof(DeferredPendingIndexEntity.Remove),
                            item.SequenceNumber);
                    }
                    else
                    {
                        // Keep it for retry.
                        await context.Entities.CallEntityAsync<bool>(
                            indexEntityId,
                            nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                            item.SequenceNumber);
                    }

                }

                logger.DrainCompleted(lane, processed, newlyDispatched, removed, staleRemoved, messageNotFound, capacityDenied);
            }
            finally
            {
                await context.Entities.CallEntityAsync(indexEntityId, nameof(DeferredPendingIndexEntity.ReleaseDrainLock));
            }
        }

        private static int GetMaxDrainBatch(string lane)
        {
            // Policy: Small=16, Large=3
            return lane.Equals("large", StringComparison.OrdinalIgnoreCase) ? 3 : 16;
        }
    }
}
