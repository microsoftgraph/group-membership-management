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

                return new ReceiveDeferredPendingResponse(Dispatched: request.AlreadyDispatched, ShouldRemoveFromIndex: shouldRemoveFromIndex, OrchestrationInstanceId: request.OrchestrationInstanceId, MessageNotFound: true);
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

                return new ReceiveDeferredPendingResponse(Dispatched: request.AlreadyDispatched, ShouldRemoveFromIndex: shouldRemoveFromIndex, OrchestrationInstanceId: request.OrchestrationInstanceId, MessageNotFound: true);
            }

            try
            {
                string instanceId = request.OrchestrationInstanceId;
                var dispatched = request.AlreadyDispatched;

                if (!dispatched)
                {
                    var workItem = JsonSerializer.Deserialize<OrchestratorRequest>(Encoding.UTF8.GetString(message.Body));
                    if (workItem == null)
                    {
                        await _pendingReceiver.DeadLetterMessageAsync(message, deadLetterReason: "InvalidPendingMessage", deadLetterErrorDescription: "Deserialized work item was null.");
                        _logger.DeferredDeadLetteredNullBody(request.SequenceNumber);
                        return new ReceiveDeferredPendingResponse(Dispatched: false, ShouldRemoveFromIndex: true, OrchestrationInstanceId: null, MessageNotFound: false);
                    }

                    // Deterministic instance id helps eliminate duplicates if this activity is retried.
                    instanceId = $"deferredpending_{request.RunId}_{request.SequenceNumber}";

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

                try
                {
                    await _pendingReceiver.CompleteMessageAsync(message);
                    _logger.DeferredCompleted(request.SequenceNumber);
                    return new ReceiveDeferredPendingResponse(Dispatched: dispatched, ShouldRemoveFromIndex: true, OrchestrationInstanceId: instanceId, MessageNotFound: false);
                }
                catch (Exception ex)
                {
                    // Orchestration is dispatched, but message settle failed. Keep index entry for retry.
                    _logger.DeferredCompleteFailed(request.SequenceNumber, ex.Message);

                    return new ReceiveDeferredPendingResponse(Dispatched: dispatched, ShouldRemoveFromIndex: false, OrchestrationInstanceId: instanceId, MessageNotFound: false);
                }
            }
            catch (Exception ex)
            {
                // If the payload is invalid, dead-letter it so we don't spin forever.
                try
                {
                    await _pendingReceiver.DeadLetterMessageAsync(message, deadLetterReason: "InvalidPendingMessage", deadLetterErrorDescription: ex.Message);
                    _logger.DeferredDeadLetteredInvalidPayload(ex, request.SequenceNumber, ex.Message);
                }
                catch
                {
                    // If dead-letter fails, let the lock expire and retry later.
                    return new ReceiveDeferredPendingResponse(Dispatched: false, ShouldRemoveFromIndex: false, OrchestrationInstanceId: null, MessageNotFound: false);
                }

                return new ReceiveDeferredPendingResponse(Dispatched: false, ShouldRemoveFromIndex: true, OrchestrationInstanceId: null, MessageNotFound: false);
            }
        }
    }
}
