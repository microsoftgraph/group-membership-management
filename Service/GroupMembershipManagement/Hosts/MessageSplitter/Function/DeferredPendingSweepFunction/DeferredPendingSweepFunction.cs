// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Configuration;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class DeferredPendingSweepFunction
    {
        private readonly IConfiguration _configuration;
        private readonly RunLimiterSettings _runLimiterSettings;
        private readonly ILoggingRepository _loggingRepository;

        public DeferredPendingSweepFunction(IConfiguration configuration, RunLimiterSettings runLimiterSettings, ILoggingRepository loggingRepository)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _runLimiterSettings = runLimiterSettings ?? throw new ArgumentNullException(nameof(runLimiterSettings));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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
            await _loggingRepository.LogMessageAsync(
                new LogMessage { Message = $"DeferredPendingSweep timer fired; scheduling sweep lane={lane}" },
                VerbosityLevel.INFO);
            await durableClient.ScheduleNewOrchestrationInstanceAsync(
                nameof(DeferredPendingSweepOrchestratorFunction),
                new DeferredPendingSweepRequest(lane));
        }
    }

    public class DeferredPendingSweepOrchestratorFunction
    {
        [Function(nameof(DeferredPendingSweepOrchestratorFunction))]
        public async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<DeferredPendingSweepRequest>();
            var lane = (request?.LaneSize ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lane))
            {
                return;
            }

            var utcNow = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

            await context.CallActivityAsync(
                nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = new LogMessage { Message = $"DeferredPendingSweep: start lane={lane}" },
                    Verbosity = VerbosityLevel.INFO
                });

            // Prune stale leases.
            var limiterEntityId = new EntityInstanceId(nameof(RunLimiter), lane);
            var prunedLeases = await context.Entities.CallEntityAsync<int>(limiterEntityId, nameof(RunLimiter.Prune), utcNow);

            // Prune old index entries
            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            const int maxIndexAgeMinutes = 60;
            var prunedItems = await context.Entities.CallEntityAsync<List<DeferredPendingItem>>(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.PruneOlderThanMinutes),
                new PruneOlderThanMinutesRequest(utcNow, maxIndexAgeMinutes));

            // Set pruned jobs to Error status.
            foreach (var item in prunedItems)
            {
                await context.CallActivityAsync(
                    nameof(JobStatusUpdaterFunction),
                    new JobStatusUpdaterRequest
                    {
                        SyncJob = new SyncJob { Id = item.JobId, RunId = item.RunId },
                        Status = SyncStatus.Error
                    });

                await context.CallActivityAsync(
                    nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage
                        {
                            Message = $"DeferredPendingSweep: pruned stale index entry and set job to Error; seq={item.SequenceNumber} jobId={item.JobId} lane={lane}",
                            RunId = item.RunId
                        },
                        Verbosity = VerbosityLevel.INFO
                    });
            }

            await context.CallActivityAsync(
                nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = new LogMessage { Message = $"DeferredPendingSweep: prunedExpiredLeases={prunedLeases} prunedOldIndexItems={prunedItems.Count} lane={lane}" },
                    Verbosity = VerbosityLevel.INFO
                });

            // Kick drain.
            await context.CallSubOrchestratorAsync(nameof(DeferredPendingDrainOrchestrator), new DeferredPendingDrainRequest(lane));
        }
    }
}
