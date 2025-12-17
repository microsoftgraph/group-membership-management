// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using GraphUpdater.QueueMessageOrchestrator;
using System.Collections.Generic;
using Models.ServiceBus;

namespace Hosts.GraphUpdater
{
    public class MessageReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ServiceBusClient _serviceBusClient;

        public MessageReaderFunction(ILoggingRepository loggingRepository, ServiceBusClient serviceBusClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        }

        [Function(nameof(MessageReaderFunction))]
        public async Task<OrchestratorRequest> GetSyncJobAsync([ActivityTrigger] QueueMessageOrchestratorRequest input)
        {
            OrchestratorRequest response = null;

            if (!input.IsMultiLaneEnabled)
            {
                var membership = await ProcessRequestAsync<MembershipHttpRequest>(input);
                if (membership != null)
                    response = new OrchestratorRequest(membership);
            }
            else
            {
                var membership = await ProcessRequestAsync<GroupMembership>(input);
                if (membership != null)
                    response = new OrchestratorRequest(membership);
            }

            return response;
        }

        private async Task<T> ProcessRequestAsync<T>(QueueMessageOrchestratorRequest input) where T : class
        {
            var additionalProperties = new Dictionary<string, string> { { "Instance", input.SubscriptionName } };
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(MessageReaderFunction)} function started",
                DynamicProperties = additionalProperties
            }, VerbosityLevel.DEBUG);

            T request = null;
            var message = await GetServiceBusMessageAsync(input);
            if (message != null)
            {
                request = JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(message.Body));
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(MessageReaderFunction)} function started",
                DynamicProperties = additionalProperties
            }, VerbosityLevel.DEBUG);

            return request;
        }

        private async Task<ServiceBusReceivedMessage> GetServiceBusMessageAsync(QueueMessageOrchestratorRequest input)
        {
            var receiver = _serviceBusClient.CreateReceiver(input.TopicName, input.SubscriptionName);
            var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));

            if (message != null)
                await receiver.CompleteMessageAsync(message);

            await receiver.DisposeAsync();

            return message;
        }
    }
}
