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
    public class DeferredPendingEnqueueOrchestrator
    {
        [Function(nameof(DeferredPendingEnqueueOrchestrator))]
        public async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<DeferredPendingEnqueueRequest>();
            var lane = (request?.LaneSize ?? string.Empty).ToLowerInvariant();
            var logger = context.CreateReplaySafeLogger("MessageSplitter.DeferredPendingEnqueueOrchestrator");

            if (string.IsNullOrWhiteSpace(lane) || request == null)
            {
                logger.EnqueueIndexed(0, string.Empty);
                return;
            }

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var utcNow = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

            await context.Entities.CallEntityAsync(
                indexEntityId,
                nameof(DeferredPendingIndexEntity.Add),
                new AddDeferredPendingRequest(request.SequenceNumber, request.RunId, utcNow, request.JobId));

            logger.EnqueueIndexed(request.SequenceNumber, lane);

            await context.CallSubOrchestratorAsync(nameof(DeferredPendingDrainOrchestrator), new DeferredPendingDrainRequest(lane));
        }
    }
}
