// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models.ServiceBus;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class LeaseRenewListenerFunction
    {
        private readonly ILogger<LeaseRenewListenerFunction> _logger;

        public LeaseRenewListenerFunction(ILogger<LeaseRenewListenerFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(LeaseRenewListenerFunction))]
        public async Task ProcessLeaseRenewAsync(
            [ServiceBusTrigger(topicName: "%serviceBusMessageSplitterTopic%", subscriptionName: "%messageSplitterLeaseRenewSubscription%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            ServiceBusMessageActions actions,
            [DurableClient] DurableTaskClient durableClient)
        {
            MessageSplitterLeaseRenewSignal signal;
            try
            {
                if (message.Body != null && message.Body.ToMemory().Length > 0)
                {
                    signal = JsonSerializer.Deserialize<MessageSplitterLeaseRenewSignal>(Encoding.UTF8.GetString(message.Body));
                }
                else
                {
                    var runId = Guid.Parse(message.ApplicationProperties["RunId"].ToString());
                    signal = new MessageSplitterLeaseRenewSignal(runId, "large", leaseTimeoutMinutes: 15);
                }
            }
            catch (Exception ex)
            {
                _logger.FailedToParseLeaseRenewSignal(ex, ex.Message);
                await actions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidLeaseRenewMessage", deadLetterErrorDescription: ex.Message);
                return;
            }

            _logger.ProcessingLeaseRenewSignal(signal.LaneSize, signal.LeaseTimeoutMinutes);
            await durableClient.ScheduleNewOrchestrationInstanceAsync(nameof(LeaseRenewOrchestratorFunction), signal);
            await actions.CompleteMessageAsync(message);
        }
    }
}
