// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Models;
using System;
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
                return;
            }

            var logger = context.CreateReplaySafeLogger("MessageSplitter.DeferredPendingDrainOrchestrator");

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);
            var utcNow = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

            // Take a single item instead of a batch. This eliminates the class of bugs
            // caused by batch-take with early termination (lock leaks on remaining items,
            // capacity slot waste, cascading sweep prunes).
            var item = await context.Entities.CallEntityAsync<DeferredPendingItem>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.TakeNext),
                new TakeNextRequest(utcNow, 60));

            if (item == null)
            {
                logger.DrainNoItems(lane);
                return;
            }

            logger.DrainStarted(lane, item.SequenceNumber);

            var leaseAcquiredForDispatch = false;
            var madeProgress = false;

            try
            {
                if (!item.Dispatched)
                {
                    var lease = await context.Entities.CallEntityAsync<AcquireLeaseResponse>(
                        limiterEntityId,
                        nameof(RunLimiter.Acquire),
                        new AcquireLeaseRequest(item.RunId, _runLimiterSettings.MaxInFlightMessages, _runLimiterSettings.LeaseTimeoutMinutes, utcNow));

                    if (!lease.Acquired)
                    {
                        var shouldLog = !item.LastCapacityDeniedAtUtc.HasValue ||
                                        (utcNow - item.LastCapacityDeniedAtUtc.Value).TotalSeconds > 60;

                        if (shouldLog)
                        {
                            logger.DrainNoCapacity(lane, lease.InFlightCount, item.RunId, item.SequenceNumber);
                        }

                        await context.Entities.CallEntityAsync<bool>(
                            indexEntityId,
                            nameof(DeferredPendingIndexEntity.MarkCapacityDeniedAndRelease),
                            new MarkCapacityDeniedRequest(item.SequenceNumber, utcNow));

                        // Don't continue — wait for CompletionListener to trigger the next drain
                        // when a GU job finishes and capacity frees up.
                        return;
                    }

                    leaseAcquiredForDispatch = true;
                }

                ReceiveDeferredPendingResponse received;
                received = await context.CallActivityAsync<ReceiveDeferredPendingResponse>(
                    nameof(ReceiveDeferredPendingFunction),
                    new ReceiveDeferredPendingRequest(item.SequenceNumber, item.RunId, item.Dispatched, item.OrchestrationInstanceId));

                if (received.Dispatched && !item.Dispatched)
                {
                    await context.Entities.CallEntityAsync<bool>(
                        indexEntityId,
                        nameof(DeferredPendingIndexEntity.MarkDispatched),
                        new MarkDeferredPendingDispatchedRequest(item.SequenceNumber, received.OrchestrationInstanceId));
                    madeProgress = true;
                }

                if (!received.Dispatched && leaseAcquiredForDispatch)
                {
                    await context.Entities.CallEntityAsync<bool>(limiterEntityId, nameof(RunLimiter.Release), item.RunId);
                }

                var shouldRemove = received.ShouldRemoveFromIndex;

                if (!shouldRemove && received.MessageNotFound
                    && (utcNow - item.EnqueuedAtUtc).TotalMinutes > 5)
                {
                    shouldRemove = true;
                    logger.DrainRemovingStaleEntry(
                        $"{(utcNow - item.EnqueuedAtUtc).TotalMinutes:F1}",
                        item.SequenceNumber,
                        item.JobId,
                        lane);
                }

                if (shouldRemove)
                {
                    await context.Entities.CallEntityAsync<bool>(
                        indexEntityId,
                        nameof(DeferredPendingIndexEntity.Remove),
                        item.SequenceNumber);
                    madeProgress = true;
                }
                else
                {
                    // Item stays in the index. ReleaseInProgress moves it to the tail
                    // so the next TakeNext picks a different item, preventing head-of-line blocking.
                    await context.Entities.CallEntityAsync<bool>(
                        indexEntityId,
                        nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                        item.SequenceNumber);
                }

                var result = shouldRemove ? "removed" : received.Dispatched ? "dispatched" : "kept";
                logger.DrainItemProcessed(lane, item.SequenceNumber, result, received.MessageNotFound);

                // Only continue draining when forward progress was made (item removed or
                // newly dispatched). If the item was kept (e.g., message not found < 5 min,
                // already-dispatched but still running), stop and let the next natural trigger
                // (CompletionListener, Enqueue, Sweep) resume the drain. This prevents hot-looping
                // on retryable items while still efficiently draining pending work.
                if (madeProgress)
                {
                    context.ContinueAsNew(input);
                }
            }
            catch (Exception ex)
            {
                if (leaseAcquiredForDispatch)
                {
                    await context.Entities.CallEntityAsync<bool>(limiterEntityId, nameof(RunLimiter.Release), item.RunId);
                }

                await context.Entities.CallEntityAsync<bool>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.ReleaseInProgress),
                    item.SequenceNumber);

                logger.DrainReceiveFailed(ex, lane, item.RunId, item.SequenceNumber);
                throw;
            }
        }
    }
}
