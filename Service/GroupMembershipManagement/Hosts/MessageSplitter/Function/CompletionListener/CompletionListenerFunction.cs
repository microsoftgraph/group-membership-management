// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class CompletionListenerFunction
    {
        private readonly ILogger<CompletionListenerFunction> _logger;

        public CompletionListenerFunction(ILogger<CompletionListenerFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                _logger.FailedToParseCompletionSignal(ex, ex.Message);
                await actions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidCompletionMessage", deadLetterErrorDescription: ex.Message);
                return;
            }

            _logger.ProcessingCompletionSignal(signal.LaneSize);
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
            var logger = context.CreateReplaySafeLogger("MessageSplitter.CompletionOrchestratorFunction");
            var entityId = new EntityInstanceId(nameof(RunLimiter), request.LaneSize.ToLowerInvariant());
            var released = await context.Entities.CallEntityAsync<bool>(entityId, nameof(RunLimiter.Release), request.RunId);

            var drainAction = released ? "Starting deferred drain." : "Skipping drain (nothing released).";

            logger.CompletionProcessed(request.LaneSize.ToLowerInvariant(), released, drainAction);

            if (released)
            {
                // Drain deferred pending work now that capacity is available.
                await context.CallSubOrchestratorAsync(nameof(DeferredPendingDrainOrchestrator), new DeferredPendingDrainRequest(request.LaneSize.ToLowerInvariant()));
            }
        }
    }
}
