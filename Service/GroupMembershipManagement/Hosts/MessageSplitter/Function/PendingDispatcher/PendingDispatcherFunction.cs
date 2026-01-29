// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using MessageSplitter.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Models;
using Repositories.Contracts;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class PendingDispatcherFunction
    {
        private const int MaxDeliveryCount = 10;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IMessageSplitterService _messageSplitterService;

        public PendingDispatcherFunction(
            ILoggingRepository loggingRepository,
            IMessageSplitterService messageSplitterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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
            var runId = Guid.Empty;
            var lane = string.Empty;

            try
            {
                request = JsonSerializer.Deserialize<OrchestratorRequest>(Encoding.UTF8.GetString(message.Body));
                runId = request?.MembershipRequest?.SyncJob?.RunId.GetValueOrDefault(Guid.Empty) ?? Guid.Empty;
                lane = (request?.CurrentLaneSize ?? string.Empty).ToLowerInvariant();

                if (request?.MembershipRequest?.SyncJob != null)
                {
                    _loggingRepository.UpsertSyncJobProperties(runId, request.MembershipRequest.SyncJob.ToDictionary());
                }

                await _loggingRepository.LogMessageAsync(
                    new LogMessage { Message = $"{nameof(PendingDispatcherFunction)} received pending message; lane={lane} seq={message.SequenceNumber}", RunId = runId },
                    VerbosityLevel.INFO);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Failed to deserialize pending work item: {ex.Message}", RunId = runId }, VerbosityLevel.INFO);
                await actions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidPendingMessage", deadLetterErrorDescription: ex.Message);
                return;
            }

            // Schedule the orchestrator FIRST, before deferring.
            // If this fails (e.g., gRPC timeout), the message is NOT deferred and Service Bus will retry delivery.
            // This prevents messages from being stranded in a deferred state without being indexed.
            string orchestrationInstanceId;
            try
            {
                orchestrationInstanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
                    nameof(DeferredPendingEnqueueOrchestrator),
                    new DeferredPendingEnqueueRequest(lane, message.SequenceNumber, runId));

                await _loggingRepository.LogMessageAsync(
                    new LogMessage { Message = $"Scheduled pending drain orchestrator; instanceId={orchestrationInstanceId} lane={lane} seq={message.SequenceNumber}", RunId = runId },
                    VerbosityLevel.INFO);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(
                    new LogMessage
                    {
                        Message = $"Failed to schedule orchestrator for pending message; lane={lane} seq={message.SequenceNumber} deliveryCount={message.DeliveryCount} err={ex.Message}",
                        RunId = runId
                    },
                    VerbosityLevel.INFO);

                // If this is the last delivery attempt, set job to Error and dead-letter the message
                if (message.DeliveryCount >= MaxDeliveryCount)
                {
                    await _loggingRepository.LogMessageAsync(
                        new LogMessage
                        {
                            Message = $"Max delivery count reached; setting job to Error and dead-lettering message; lane={lane} seq={message.SequenceNumber}",
                            RunId = runId
                        },
                        VerbosityLevel.INFO);

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

                await _loggingRepository.LogMessageAsync(
                    new LogMessage { Message = $"Defer succeeded for pending message; lane={lane} seq={message.SequenceNumber}", RunId = runId },
                    VerbosityLevel.INFO);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(
                    new LogMessage
                    {
                        Message = $"Failed to defer pending message; lane={lane} seq={message.SequenceNumber} deliveryCount={message.DeliveryCount} lockedUntilUtc={message.LockedUntil:O} errType={ex.GetType().FullName} err={ex.Message}",
                        RunId = runId
                    },
                    VerbosityLevel.INFO);

                // Rethrow so Service Bus trigger retries; deferral is required for drain-by-sequence.
                throw;
            }
        }
    }
}
