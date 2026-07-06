// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class ReceiveDeferredPendingFunction
    {
        private readonly ServiceBusReceiver _pendingReceiver;
        private readonly ILogger<ReceiveDeferredPendingFunction> _logger;

        public ReceiveDeferredPendingFunction(
            [FromKeyedServices("messageSplitterPendingReceiver")] ServiceBusReceiver pendingReceiver,
            ILogger<ReceiveDeferredPendingFunction> logger)
        {
            _pendingReceiver = pendingReceiver ?? throw new ArgumentNullException(nameof(pendingReceiver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(ReceiveDeferredPendingFunction))]
        public async Task<ReceiveDeferredPendingResponse> RunAsync(
            [ActivityTrigger] ReceiveDeferredPendingRequest request,
            [DurableClient] DurableTaskClient durableClient)
        {
            // Receive deferred message by sequence number from the pending subscription.
            ServiceBusReceivedMessage message;
            try
            {
                message = await _pendingReceiver.ReceiveDeferredMessageAsync(request.SequenceNumber);
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageNotFound)
            {
                // This can legitimately happen if drain runs before the message is deferred.
                // Only remove the index entry if we already dispatched the orchestration.
                var shouldRemoveFromIndex = request.AlreadyDispatched;
                _logger.DeferredMessageNotFound(shouldRemoveFromIndex ? "remove" : "retry", request.SequenceNumber, ex.Message);

                var peerOrchestrationExists = await PeerOrchestrationExistsAsync(durableClient, request);
                return new ReceiveDeferredPendingResponse(Dispatched: request.AlreadyDispatched, ShouldRemoveFromIndex: shouldRemoveFromIndex, OrchestrationInstanceId: request.OrchestrationInstanceId, MessageNotFound: true, PeerOrchestrationExists: peerOrchestrationExists);
            }
            catch (Exception ex)
            {
                _logger.DeferredReceiveFailed(ex, request.SequenceNumber, ex.Message);

                return new ReceiveDeferredPendingResponse(Dispatched: request.AlreadyDispatched, ShouldRemoveFromIndex: false, OrchestrationInstanceId: request.OrchestrationInstanceId, MessageNotFound: false);
            }

            if (message == null)
            {
                // This can happen if drain runs before the message is deferred.
                // Only remove the index entry if we already dispatched the orchestration.
                var shouldRemoveFromIndex = request.AlreadyDispatched;
                _logger.DeferredMessageNull(shouldRemoveFromIndex ? "remove" : "retry", request.SequenceNumber);

                var peerOrchestrationExists = await PeerOrchestrationExistsAsync(durableClient, request);
                return new ReceiveDeferredPendingResponse(Dispatched: request.AlreadyDispatched, ShouldRemoveFromIndex: shouldRemoveFromIndex, OrchestrationInstanceId: request.OrchestrationInstanceId, MessageNotFound: true, PeerOrchestrationExists: peerOrchestrationExists);
            }

            // Step 1: Deserialize the payload. Payload errors are permanent — dead-letter and remove.
            // Infrastructure errors (gRPC, storage) must propagate so the drain retries the item.
            string instanceId = request.OrchestrationInstanceId;
            var dispatched = request.AlreadyDispatched;

            if (!dispatched)
            {
                OrchestratorRequest workItem;
                try
                {
                    workItem = JsonSerializer.Deserialize<OrchestratorRequest>(Encoding.UTF8.GetString(message.Body));
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or DecoderFallbackException)
                {
                    // Permanent payload error — dead-letter so we don't spin forever.
                    try
                    {
                        await _pendingReceiver.DeadLetterMessageAsync(message, deadLetterReason: "InvalidPendingMessage", deadLetterErrorDescription: ex.Message);
                        _logger.DeferredDeadLetteredInvalidPayload(ex, request.SequenceNumber, ex.Message);
                    }
                    catch
                    {
                        return new ReceiveDeferredPendingResponse(Dispatched: false, ShouldRemoveFromIndex: false, OrchestrationInstanceId: null, MessageNotFound: false);
                    }

                    return new ReceiveDeferredPendingResponse(Dispatched: false, ShouldRemoveFromIndex: true, OrchestrationInstanceId: null, MessageNotFound: false);
                }

                if (workItem == null)
                {
                    await _pendingReceiver.DeadLetterMessageAsync(message, deadLetterReason: "InvalidPendingMessage", deadLetterErrorDescription: "Deserialized work item was null.");
                    _logger.DeferredDeadLetteredNullBody(request.SequenceNumber);
                    return new ReceiveDeferredPendingResponse(Dispatched: false, ShouldRemoveFromIndex: true, OrchestrationInstanceId: null, MessageNotFound: false);
                }

                // Deterministic instance id helps eliminate duplicates if this activity is retried.
                instanceId = DeterministicInstanceId(request.RunId, request.SequenceNumber);

                // Infrastructure errors from GetInstanceAsync / ScheduleNewOrchestrationInstanceAsync
                // propagate to the drain's catch block, which releases the lease + InProgress and throws.
                // The item stays in the index for retry by the next drain cycle.
                var existing = await durableClient.GetInstanceAsync(instanceId);
                if (existing == null)
                {
                    await durableClient.ScheduleNewOrchestrationInstanceAsync(
                        nameof(OrchestratorFunction),
                        workItem,
                        new StartOrchestrationOptions { InstanceId = instanceId });

                    _logger.DeferredDispatched(instanceId, request.SequenceNumber);
                }

                dispatched = true;
            }

            // Step 2: Complete the SB message. Failure here is transient — keep index entry for retry.
            try
            {
                await _pendingReceiver.CompleteMessageAsync(message);
                _logger.DeferredCompleted(request.SequenceNumber);
                return new ReceiveDeferredPendingResponse(Dispatched: dispatched, ShouldRemoveFromIndex: true, OrchestrationInstanceId: instanceId, MessageNotFound: false);
            }
            catch (Exception ex)
            {
                _logger.DeferredCompleteFailed(request.SequenceNumber, ex.Message);
                return new ReceiveDeferredPendingResponse(Dispatched: dispatched, ShouldRemoveFromIndex: false, OrchestrationInstanceId: instanceId, MessageNotFound: false);
            }
        }

        /// <summary>
        /// Determines, without a race, whether a peer drain already dispatched this exact item when the
        /// deferred message could not be received ("message not found"). Returns <c>true</c> only if the
        /// deterministic GraphUpdater orchestration already exists.
        /// </summary>
        /// <remarks>
        /// The deterministic instance is created (<see cref="DurableTaskClient.ScheduleNewOrchestrationInstanceAsync(string, object, StartOrchestrationOptions, System.Threading.CancellationToken)"/>)
        /// before the peer completes the Service Bus message, which happens-before this drain can observe the
        /// message as "not found". Its presence therefore authoritatively means a peer dispatched the item —
        /// it is never a genuine orphan — and lets the orchestrator suppress the false-Error the age heuristic
        /// alone cannot rule out. Infrastructure errors from GetInstanceAsync propagate exactly like the
        /// dispatch-path lookup, so the drain retries the item rather than deciding on stale information.
        /// </remarks>
        private static async Task<bool> PeerOrchestrationExistsAsync(DurableTaskClient durableClient, ReceiveDeferredPendingRequest request)
        {
            // A drain that itself already dispatched is not a stale loser; no peer lookup is needed (and the
            // orchestrator removes the entry rather than evaluating the orphan gate for it).
            if (request.AlreadyDispatched)
            {
                return false;
            }

            var existing = await durableClient.GetInstanceAsync(DeterministicInstanceId(request.RunId, request.SequenceNumber));
            return existing != null;
        }

        private static string DeterministicInstanceId(Guid runId, long sequenceNumber) =>
            $"deferredpending_{runId}_{sequenceNumber}";
    }
}
