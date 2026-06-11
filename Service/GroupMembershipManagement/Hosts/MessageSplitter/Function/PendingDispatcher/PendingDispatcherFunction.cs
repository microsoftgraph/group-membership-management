// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using MessageSplitter.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class PendingDispatcherFunction
    {
        private const int MaxDeliveryCount = 10;
        private readonly ILogger<PendingDispatcherFunction> _logger;
        private readonly IMessageSplitterService _messageSplitterService;

        public PendingDispatcherFunction(
            ILogger<PendingDispatcherFunction> logger,
            IMessageSplitterService messageSplitterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageSplitterService = messageSplitterService ?? throw new ArgumentNullException(nameof(messageSplitterService));
        }

        [Function(nameof(PendingDispatcherFunction))]
        public async Task ProcessPendingAsync(
            [ServiceBusTrigger(topicName: "%serviceBusMessageSplitterTopic%", subscriptionName: "%messageSplitterPendingSubscription%", Connection = "gmmServiceBus")]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions actions,
            [DurableClient] DurableTaskClient durableClient)
        {
            OrchestratorRequest request;
            var lane = string.Empty;

            try
            {
                request = JsonSerializer.Deserialize<OrchestratorRequest>(Encoding.UTF8.GetString(message.Body));
                lane = (request?.CurrentLaneSize ?? string.Empty).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                _logger.PendingDeserializationFailed(ex, ex.Message);
                await actions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidPendingMessage", deadLetterErrorDescription: ex.Message);
                return;
            }

            using (_logger.BeginSyncJobScope(request?.MembershipRequest?.SyncJob))
            {
                _logger.PendingMessageReceived(lane, message.SequenceNumber);

                // Schedule the orchestrator FIRST, before deferring.
                // If this fails (e.g., gRPC timeout), the message is NOT deferred and Service Bus will retry delivery.
                // This prevents messages from being stranded in a deferred state without being indexed.
                string orchestrationInstanceId;
                try
                {
                    var jobId = request.MembershipRequest.SyncJob.Id;
                    var runId = request.MembershipRequest.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
                    orchestrationInstanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
                        nameof(DeferredPendingEnqueueOrchestrator),
                        new DeferredPendingEnqueueRequest(lane, message.SequenceNumber, runId, jobId));

                    _logger.PendingDrainScheduled(orchestrationInstanceId, lane, message.SequenceNumber);
                }
                catch (Exception ex)
                {
                    _logger.PendingScheduleFailed(ex, lane, message.SequenceNumber, message.DeliveryCount, ex.Message);

                    // If this is the last delivery attempt, set job to Error and dead-letter the message
                    if (message.DeliveryCount >= MaxDeliveryCount)
                    {
                        _logger.PendingMaxDeliveryReached(lane, message.SequenceNumber);

                        await _messageSplitterService.UpdateJobStatusAsync(request.MembershipRequest.SyncJob.Id, SyncStatus.Error);
                        await actions.DeadLetterMessageAsync(message, deadLetterReason: "MaxDeliveryCountExceeded", deadLetterErrorDescription: ex.Message);
                        return;
                    }

                    // Rethrow - message is NOT deferred, Service Bus will retry
                    throw;
                }

                // Now defer the message. If this fails, the orchestrator will handle the "message not found" case
                // gracefully by removing it from the index.
                try
                {
                    await actions.DeferMessageAsync(message);

                    _logger.PendingDeferSucceeded(lane, message.SequenceNumber);
                }
                catch (Exception ex)
                {
                    _logger.PendingDeferFailed(ex, lane, message.SequenceNumber, message.DeliveryCount, message.LockedUntil.ToString("O"), ex.GetType().FullName, ex.Message);

                    // Rethrow so Service Bus trigger retries; deferral is required for drain-by-sequence.
                    throw;
                }
            }
        }
    }
}
