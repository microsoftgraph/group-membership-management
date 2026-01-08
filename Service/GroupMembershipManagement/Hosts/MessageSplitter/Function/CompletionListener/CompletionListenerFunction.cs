// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class CompletionListenerFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public CompletionListenerFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(CompletionListenerFunction))]
        public async Task ProcessCompletionAsync(
            [ServiceBusTrigger(topicName: "%serviceBusMessageSplitterTopic%", subscriptionName: "%messageSplitterCompletionSubscription%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            ServiceBusMessageActions actions,
            [DurableClient] DurableTaskClient durableClient)
        {
            MessageSplitterCompletionSignal signal;
            try
            {
                // Prefer body, but fall back to app props for resilience.
                if (message.Body != null && message.Body.ToMemory().Length > 0)
                {
                    signal = JsonSerializer.Deserialize<MessageSplitterCompletionSignal>(Encoding.UTF8.GetString(message.Body));
                }
                else
                {
                    var runId = Guid.Parse(message.ApplicationProperties["RunId"].ToString());
                    var laneSize = GetLaneSizeFromProperties(message.ApplicationProperties);
                    signal = new MessageSplitterCompletionSignal(runId, laneSize);
                }
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Failed to parse completion signal: {ex.Message}" }, VerbosityLevel.INFO);
                await actions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidCompletionMessage", deadLetterErrorDescription: ex.Message);
                return;
            }

            await _loggingRepository.LogMessageAsync(
                new LogMessage { Message = $"Processing completion signal; lane={signal.LaneSize}", RunId = signal.RunId },
                VerbosityLevel.INFO);
            await durableClient.ScheduleNewOrchestrationInstanceAsync(nameof(CompletionOrchestratorFunction), signal);
            await actions.CompleteMessageAsync(message);
        }

        private static string GetLaneSizeFromProperties(System.Collections.Generic.IReadOnlyDictionary<string, object> applicationProperties)
        {
            if (applicationProperties.TryGetValue("MessageType", out var messageTypeObj) && messageTypeObj != null)
            {
                var messageType = messageTypeObj.ToString();
                const string prefix = "completion_";
                if (!string.IsNullOrWhiteSpace(messageType) && messageType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return messageType.Substring(prefix.Length);
                }
            }

            throw new InvalidOperationException("Completion message did not contain completion_<Lane> MessageType.");
        }
    }

    public class CompletionOrchestratorFunction
    {
        private readonly RunLimiterSettings _runLimiterSettings;

        public CompletionOrchestratorFunction(RunLimiterSettings runLimiterSettings)
        {
            _runLimiterSettings = runLimiterSettings ?? throw new ArgumentNullException(nameof(runLimiterSettings));
        }

        [Function(nameof(CompletionOrchestratorFunction))]
        public async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            if (!_runLimiterSettings.IsEnabled)
            {
                return;
            }

            var request = context.GetInput<MessageSplitterCompletionSignal>();
            var entityId = new EntityInstanceId(nameof(RunLimiter), request.LaneSize.ToLowerInvariant());
            var released = false;
            await using (await context.Entities.LockEntitiesAsync(entityId))
            {
                released = await context.Entities.CallEntityAsync<bool>(entityId, nameof(RunLimiter.Release), request.RunId);
            }

            var drainAction = released ? "Starting deferred drain." : "Skipping drain (nothing released).";

            await context.CallActivityAsync(
                nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = new LogMessage
                    {
                        Message = $"Completion processed; lane={request.LaneSize.ToLowerInvariant()} released={released}. {drainAction}",
                        RunId = request.RunId
                    },
                    Verbosity = VerbosityLevel.INFO
                });

            if (released)
            {
                // Drain deferred pending work now that capacity is available.
                await context.CallSubOrchestratorAsync(nameof(DeferredPendingDrainOrchestrator), new DeferredPendingDrainRequest(request.LaneSize.ToLowerInvariant()));
            }
        }
    }
}
