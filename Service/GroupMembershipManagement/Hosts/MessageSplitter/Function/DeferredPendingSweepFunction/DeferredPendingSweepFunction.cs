// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class DeferredPendingSweepFunction
    {
        private readonly IConfiguration _configuration;
        private readonly RunLimiterSettings _runLimiterSettings;
        private readonly ILogger<DeferredPendingSweepFunction> _logger;

        public DeferredPendingSweepFunction(IConfiguration configuration, RunLimiterSettings runLimiterSettings, ILogger<DeferredPendingSweepFunction> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _runLimiterSettings = runLimiterSettings ?? throw new ArgumentNullException(nameof(runLimiterSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(DeferredPendingSweepFunction))]
        public async Task RunAsync(
            [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
            [DurableClient] DurableTaskClient durableClient)
        {
            if (!_runLimiterSettings.IsEnabled)
            {
                return;
            }

            var lane = CommonServices.GetValueOrThrowBase(_configuration, "messageSplitterSubscription").ToLowerInvariant();
            _logger.SweepTimerFired(lane);
            await durableClient.ScheduleNewOrchestrationInstanceAsync(
                nameof(DeferredPendingSweepOrchestratorFunction),
                new DeferredPendingSweepRequest(lane));
        }
    }

    public class DeferredPendingSweepOrchestratorFunction
    {
        private readonly RunLimiterSettings _runLimiterSettings;

        public DeferredPendingSweepOrchestratorFunction(RunLimiterSettings runLimiterSettings)
        {
            _runLimiterSettings = runLimiterSettings ?? throw new ArgumentNullException(nameof(runLimiterSettings));
        }

        [Function(nameof(DeferredPendingSweepOrchestratorFunction))]
        public async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<DeferredPendingSweepRequest>();
            var lane = (request?.LaneSize ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lane))
            {
                return;
            }

            var logger = context.CreateReplaySafeLogger("MessageSplitter.DeferredPendingSweepOrchestratorFunction");
            var utcNow = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

            logger.SweepStarted(lane);

            // Prune stale leases.
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);
            var prunedLeases = await context.Entities.CallEntityAsync<int>(limiterEntityId, nameof(RunLimiter.Prune), utcNow);

            // Check current capacity after pruning expired leases.
            // If all slots are occupied, the downstream updater is actively processing —
            // do not prune deferred items; they will drain naturally when capacity frees.
            var limiterState = await context.Entities.CallEntityAsync<RunLimiterState>(limiterEntityId, nameof(RunLimiter.GetState));
            var activeLeases = limiterState?.Leases?.Count ?? 0;
            var maxInFlight = _runLimiterSettings.MaxInFlightMessages;

            var prunedItemCount = 0;
            if (activeLeases >= maxInFlight)
            {
                logger.SweepSkippedAtCapacity(lane, activeLeases, maxInFlight);
            }
            else
            {
                // Capacity is available but items are still waiting — they may be stuck.
                // Prune entries older than the configured age threshold.
                var maxAgeMinutes = _runLimiterSettings.MaxPendingAgeMinutes > 0
                    ? _runLimiterSettings.MaxPendingAgeMinutes
                    : 60;

                var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
                var prunedItems = await context.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.PruneOlderThanMinutes),
                    new PruneOlderThanMinutesRequest(utcNow, maxAgeMinutes));

                // Set pruned jobs to Error status.
                var statusUpdateFailures = 0;
                foreach (var item in prunedItems)
                {
                    try
                    {
                        await context.CallActivityAsync(
                            nameof(JobStatusUpdaterFunction),
                            new JobStatusUpdaterRequest
                            {
                                SyncJob = new SyncJob { Id = item.JobId, RunId = item.RunId },
                                Status = SyncStatus.Error
                            });
                    }
                    catch (Exception ex)
                    {
                        statusUpdateFailures++;
                        logger.SweepJobStatusUpdateFailed(item.SequenceNumber, item.JobId, lane, ex.Message);
                        // Continue — items are already removed from the index;
                        // failing one status update should not abort the rest.
                    }

                    logger.SweepPrunedStaleEntry(item.SequenceNumber, item.JobId, lane);
                }

                if (statusUpdateFailures > 0)
                {
                    logger.SweepStatusUpdateFailures(statusUpdateFailures, prunedItems.Count, lane);
                }

                prunedItemCount = prunedItems.Count;
            }

            logger.SweepCompleted(prunedLeases, prunedItemCount, lane);

            // Kick drain.
            await context.CallSubOrchestratorAsync(nameof(DeferredPendingDrainOrchestrator), new DeferredPendingDrainRequest(lane));
        }
    }
}
