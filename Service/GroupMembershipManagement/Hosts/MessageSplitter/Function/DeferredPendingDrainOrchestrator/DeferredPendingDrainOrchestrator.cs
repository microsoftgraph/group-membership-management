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

                    // The capacity lease has been released here; clear the flag so the catch block below does
                    // not issue a second, redundant release for the same run if a later step throws.
                    leaseAcquiredForDispatch = false;
                }

                var shouldRemove = received.ShouldRemoveFromIndex;
                var confirmedOrphan = false;
                var supersededByPeer = false;

                if (!shouldRemove && received.MessageNotFound
                    && (utcNow - item.EnqueuedAtUtc).TotalMinutes > _runLimiterSettings.MaxPendingAgeMinutes)
                {
                    // The message is gone and the entry has outlived the pending-age window. This is EITHER a
                    // genuine orphan OR the benign artifact of a concurrent drain that already re-took,
                    // dispatched, and removed this entry after our 60-second in-progress lease expired under
                    // load — leaving us a stale, never-dispatched snapshot and a "message not found". Deciding
                    // solely on our snapshot would spuriously fail a job the peer dispatched successfully.
                    if (received.PeerOrchestrationExists)
                    {
                        // Authoritative and race-free: the peer created the deterministic GraphUpdater
                        // orchestration BEFORE it completed the Service Bus message, which happens-before our
                        // "message not found" observation. Its existence proves a peer dispatched this exact
                        // item, so it is never a genuine orphan — even in the narrow window before the peer's
                        // MarkDispatched lands on the single-threaded entity. Suppress the false Error and remove
                        // the now-redundant index entry in the shared block below, so a ghost dispatcher that
                        // never runs MarkDispatched (crashed after completing the message, or lost its activity
                        // result to an at-least-once replay) cannot leave a permanent row that the IsConfirmedOrphan
                        // fallback later mis-Errors once the peer orchestration is purged.
                        supersededByPeer = true;
                        logger.DrainItemSupersededByPeer(lane, item.RunId, item.SequenceNumber);
                    }
                    else
                    {
                        // No peer orchestration exists for this item. It is EITHER a genuine orphan OR (rarely)
                        // the artifact of a peer that dispatched and completed so long ago its orchestration was
                        // already purged. IsConfirmedOrphan re-checks on the single-threaded entity and reports
                        // an orphan only when the entry is still present and was never dispatched — the peer's
                        // MarkDispatched clears that in the purge case — so together with the instance check the
                        // Error fires only for a true orphan. It does NOT remove the entry — removal happens in
                        // the shared block below, only after the Error status update has succeeded.
                        confirmedOrphan = await context.Entities.CallEntityAsync<bool>(
                            indexEntityId,
                            nameof(DeferredPendingIndexEntity.IsConfirmedOrphan),
                            item.SequenceNumber);

                        if (confirmedOrphan)
                        {
                            var ageMinutes = (utcNow - item.EnqueuedAtUtc).TotalMinutes;

                            // Record the Error status BEFORE removing the entry from the index. If this activity
                            // throws, the entry is still present: the catch block re-tails it via ReleaseInProgress
                            // and a later drain retries it, so the job is never dropped without a status update. The
                            // removal happens in the shared block below, only after this update succeeds.
                            await context.CallActivityAsync(
                                nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = new SyncJob { Id = item.JobId, RunId = item.RunId },
                                    Status = SyncStatus.Error
                                });

                            logger.DrainErroredConfirmedOrphan(
                                lane,
                                item.JobId,
                                item.RunId,
                                item.SequenceNumber,
                                $"{ageMinutes:F1}");
                        }
                        else
                        {
                            // A peer / prior execution already dispatched or removed this entry — benign, NOT an
                            // orphan. Do not fail the job; the entry is removed in the shared block below because
                            // the dispatch is durable (the row is already dispatched or absent), so no live peer
                            // is required to clean it up.
                            supersededByPeer = true;
                            logger.DrainItemSupersededByPeer(lane, item.RunId, item.SequenceNumber);
                        }
                    }
                }

                if (shouldRemove || confirmedOrphan || supersededByPeer)
                {
                    // Remove the entry whenever this drain has resolved it: the message was completed
                    // (shouldRemove), it was a confirmed orphan we just Errored (confirmedOrphan), OR a peer /
                    // prior execution provably dispatched it (supersededByPeer). Removing in the superseded case
                    // is REQUIRED, not optional. The dispatching actor may be a ghost — a winner that created the
                    // deterministic orchestration and completed the Service Bus message but then crashed before
                    // MarkDispatched, or whose activity result was lost to an at-least-once replay — in which case
                    // no other actor is guaranteed to remove the row. Because the Sweep no longer ages out entries,
                    // a lingering present-and-never-dispatched row would otherwise (a) occupy the index permanently
                    // and steal drain cycles, and (b) be mis-Errored by the IsConfirmedOrphan fallback once the peer
                    // orchestration is purged and the instance probe can no longer see it. Remove is idempotent, so
                    // in the genuine two-drain race the winner's own later Remove simply no-ops.
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

                var result = confirmedOrphan ? "orphaned"
                    : shouldRemove ? "removed"
                    : supersededByPeer ? "superseded"
                    : received.Dispatched ? "dispatched"
                    : "kept";
                logger.DrainItemProcessed(lane, item.SequenceNumber, result, received.MessageNotFound);

                // Only continue draining when forward progress was made (item removed or
                // newly dispatched). If the item was kept (e.g., message not found within the
                // pending-age window, already-dispatched but still running), stop and let the next
                // natural trigger (CompletionListener, Enqueue, Sweep) resume the drain. This
                // prevents hot-looping on retryable items while still efficiently draining pending work.
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
