// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class MessageReaderFunction
    {
        private readonly ILogger<MessageReaderFunction> _logger;
        private readonly ServiceBusReceiver _serviceBusReceiver;

        public MessageReaderFunction(ILogger<MessageReaderFunction> logger, ServiceBusReceiver serviceBusReceiver)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceBusReceiver = serviceBusReceiver ?? throw new ArgumentNullException(nameof(serviceBusReceiver));
        }

        [Function(nameof(MessageReaderFunction))]
        public async Task<MembershipHttpRequest> GetSyncJobAsync([ActivityTrigger] object input)
        {
            _logger.FunctionStarted(nameof(MessageReaderFunction));

            MembershipHttpRequest request = null;
            var message = await _serviceBusReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));

            if (message != null)
            {
                await _serviceBusReceiver.CompleteMessageAsync(message);
                request = JsonSerializer.Deserialize<MembershipHttpRequest>(Encoding.UTF8.GetString(message.Body));
            }

            _logger.FunctionCompleted(nameof(MessageReaderFunction));
            return request;
        }
    }
}

