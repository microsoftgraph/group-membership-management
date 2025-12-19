// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
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
        private readonly ILoggingRepository _loggingRepository;

        public PendingDispatcherFunction(
            ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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

            // Index + drain kick are done via an orchestrator to keep durable operations deterministic.
            var orchestrationInstanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
                nameof(DeferredPendingEnqueueOrchestrator),
                new DeferredPendingEnqueueRequest(lane, message.SequenceNumber, runId));

            await _loggingRepository.LogMessageAsync(
                new LogMessage { Message = $"Scheduled pending drain orchestrator; instanceId={orchestrationInstanceId} lane={lane} seq={message.SequenceNumber}", RunId = runId },
                VerbosityLevel.INFO);

            // Defer the message in the pending subscription; drain will later receive by sequence number.
            try
            {
                await actions.DeferMessageAsync(message);
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

            await _loggingRepository.LogMessageAsync(
                new LogMessage { Message = $"Defer succeeded for pending message; lane={lane} seq={message.SequenceNumber}", RunId = runId },
                VerbosityLevel.INFO);
        }
    }
}
