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
    public class LeaseRenewListenerFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public LeaseRenewListenerFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Failed to parse lease renew signal: {ex.Message}" }, VerbosityLevel.INFO);
                await actions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidLeaseRenewMessage", deadLetterErrorDescription: ex.Message);
                return;
            }

            await _loggingRepository.LogMessageAsync(
                new LogMessage { Message = $"Processing lease renew signal; lane={signal.LaneSize} leaseTimeoutMinutes={signal.LeaseTimeoutMinutes}", RunId = signal.RunId },
                VerbosityLevel.INFO);
            await durableClient.ScheduleNewOrchestrationInstanceAsync(nameof(LeaseRenewOrchestratorFunction), signal);
            await actions.CompleteMessageAsync(message);
        }
    }
}
