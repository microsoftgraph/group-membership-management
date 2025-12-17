// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Models;
using Repositories.Contracts;
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

            if (string.IsNullOrWhiteSpace(lane) || request == null)
            {
                await context.CallActivityAsync(
                    nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage { Message = $"DeferredPendingEnqueue: invalid input (lane empty or request null).", RunId = request?.RunId ?? Guid.Empty },
                        Verbosity = VerbosityLevel.INFO
                    });
                return;
            }

            var indexEntityId = new EntityInstanceId(nameof(DeferredPendingIndexEntity), lane);
            var utcNow = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

            await using (await context.Entities.LockEntitiesAsync(indexEntityId))
            {
                await context.Entities.CallEntityAsync(
                    indexEntityId,
                    nameof(DeferredPendingIndexEntity.Add),
                    new AddDeferredPendingRequest(request.SequenceNumber, request.RunId, utcNow));
            }

            await context.CallActivityAsync(
                nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = new LogMessage
                    {
                        Message = $"DeferredPendingEnqueue: indexed seq={request.SequenceNumber} lane={lane}. Kicking drain.",
                        RunId = request.RunId
                    },
                    Verbosity = VerbosityLevel.INFO
                });

            await context.CallSubOrchestratorAsync(nameof(DeferredPendingDrainOrchestrator), new DeferredPendingDrainRequest(lane));
        }
    }
}
