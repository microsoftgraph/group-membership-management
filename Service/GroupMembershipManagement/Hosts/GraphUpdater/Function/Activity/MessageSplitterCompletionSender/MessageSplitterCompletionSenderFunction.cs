// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageSplitterCompletionSenderFunction
    {
        private readonly ILogger<MessageSplitterCompletionSenderFunction> _logger;
        private readonly ServiceBusSender _messageSplitterTopicSender;


        public MessageSplitterCompletionSenderFunction([FromKeyedServices("messageSplitterTopicSender")] ServiceBusSender messageSplitterTopicSender, ILogger<MessageSplitterCompletionSenderFunction> logger)
        {
            _messageSplitterTopicSender = messageSplitterTopicSender ?? throw new ArgumentNullException(nameof(messageSplitterTopicSender));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(MessageSplitterCompletionSenderFunction))]
        public async Task SendCompletionAsync([ActivityTrigger] Models.ServiceBus.MessageSplitterCompletionSignal request)
        {
            using var scope = _logger.BeginRunIdScope(request.RunId);
            _logger.FunctionStarted(nameof(MessageSplitterCompletionSenderFunction));

            var body = JsonSerializer.SerializeToUtf8Bytes(request);
            var message = new ServiceBusMessage(new BinaryData(body))
            {
                MessageId = $"completion_{request.RunId}_{request.LaneSize}"
            };

            message.ApplicationProperties["MessageType"] = $"completion_{request.LaneSize.ToLowerInvariant()}";
            message.ApplicationProperties["RunId"] = request.RunId.ToString();

            await _messageSplitterTopicSender.SendMessageAsync(message);
            _logger.CompletionMessageSent(request.RunId);
            _logger.FunctionCompleted(nameof(MessageSplitterCompletionSenderFunction));
        }
    }
}
