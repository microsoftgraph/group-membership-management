// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageRemoverFunction
    {
        private readonly ILogger<MessageRemoverFunction> _logger;
        private readonly ServiceBusClient _serviceBusClient;

        public MessageRemoverFunction(ILogger<MessageRemoverFunction> logger, ServiceBusClient serviceBusClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        }

        [Function(nameof(MessageRemoverFunction))]
        public async Task RemoveMessagesAsync([ActivityTrigger] MessageRemoverRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(MessageRemoverFunction));

            if (request.SyncJob.RunId.GetValueOrDefault() != Guid.Empty)
                await DeleteJobMessagesAsync(request);

            _logger.FunctionCompleted(nameof(MessageRemoverFunction));
        }

        private async Task DeleteJobMessagesAsync(MessageRemoverRequest request)
        {
            ServiceBusReceivedMessage message = null;
            var receiver = _serviceBusClient.CreateReceiver(request.TopicName, request.SubscriptionName);
            var startDateTime = DateTime.UtcNow;

            while (true)
            {
                message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
                if (message == null)
                    break;

                if (message.MessageId.Contains(request.SyncJob.RunId.GetValueOrDefault().ToString(), StringComparison.InvariantCultureIgnoreCase))
                {
                    await receiver.CompleteMessageAsync(message);
                }
                else
                {
                    await receiver.AbandonMessageAsync(message);
                    break;
                }

                // Break the loop if the function has been running for more than 5 minutes
                // to avoid running into the timeout limit
                if (DateTime.UtcNow.Subtract(startDateTime).TotalMinutes >= 5)
                    break;
            }

            await receiver.DisposeAsync();
        }
    }
}
