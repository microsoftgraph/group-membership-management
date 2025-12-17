// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Repositories.Contracts;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageSplitterCompletionSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ServiceBusSender _messageSplitterTopicSender;


        public MessageSplitterCompletionSenderFunction([FromKeyedServices("messageSplitterTopicSender")] ServiceBusSender messageSplitterTopicSender, ILoggingRepository loggingRepository)
        {
            _messageSplitterTopicSender = messageSplitterTopicSender ?? throw new ArgumentNullException(nameof(messageSplitterTopicSender));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(MessageSplitterCompletionSenderFunction))]
        public async Task SendCompletionAsync([ActivityTrigger] Models.ServiceBus.MessageSplitterCompletionSignal request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(MessageSplitterCompletionSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var body = JsonSerializer.SerializeToUtf8Bytes(request);
            var message = new ServiceBusMessage(new BinaryData(body))
            {
                MessageId = $"completion_{request.RunId}_{request.LaneSize}"
            };

            message.ApplicationProperties["MessageType"] = $"completion_{request.LaneSize.ToLowerInvariant()}";
            message.ApplicationProperties["RunId"] = request.RunId.ToString();

            await _messageSplitterTopicSender.SendMessageAsync(message);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Completion message for RunId {request.RunId} has been sent.", RunId = request.RunId }, VerbosityLevel.INFO);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(MessageSplitterCompletionSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}
