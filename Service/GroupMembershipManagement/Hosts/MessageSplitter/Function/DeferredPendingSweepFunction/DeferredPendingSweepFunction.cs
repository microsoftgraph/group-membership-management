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

            // Check current capacity after pruning expired leases. The sweep no longer ages out
            // deferred index entries — capacity-waiting entries are left for the drain to dispatch
            // when a slot frees up, and genuinely orphaned entries are handled by the drain. When
            // downstream is saturated we still emit informational telemetry so the at-capacity
            // window is observable.
            var limiterState = await context.Entities.CallEntityAsync<RunLimiterState>(limiterEntityId, nameof(RunLimiter.GetState));
            var activeLeases = limiterState?.Leases?.Count ?? 0;
            var maxInFlight = _runLimiterSettings.MaxInFlightMessages;

            if (activeLeases >= maxInFlight)
            {
                logger.SweepSkippedAtCapacity(lane, activeLeases, maxInFlight);
            }

            logger.SweepCompleted(prunedLeases, lane);

            // Kick drain.
            await context.CallSubOrchestratorAsync(nameof(DeferredPendingDrainOrchestrator), new DeferredPendingDrainRequest(lane));
        }
    }
}
