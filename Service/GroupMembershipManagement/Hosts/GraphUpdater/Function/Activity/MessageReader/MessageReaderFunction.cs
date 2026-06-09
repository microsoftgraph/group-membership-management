// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using GraphUpdater.QueueMessageOrchestrator;
using Models;
using Models.ServiceBus;

namespace Hosts.GraphUpdater
{
    public class MessageReaderFunction
    {
        private readonly ILogger<MessageReaderFunction> _logger;
        private readonly ServiceBusClient _serviceBusClient;

        public MessageReaderFunction(ILogger<MessageReaderFunction> logger, ServiceBusClient serviceBusClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            _logger.FunctionStarted(nameof(MessageReaderFunction));

            T request = null;
            var message = await GetServiceBusMessageAsync(input);
            if (message != null)
            {
                request = JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(message.Body));
            }

            _logger.FunctionCompleted(nameof(MessageReaderFunction));

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
