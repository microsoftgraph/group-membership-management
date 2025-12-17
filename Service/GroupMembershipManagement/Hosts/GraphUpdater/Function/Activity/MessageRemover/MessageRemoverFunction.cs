// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageRemoverFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ServiceBusClient _serviceBusClient;

        public MessageRemoverFunction(ILoggingRepository loggingRepository, ServiceBusClient serviceBusClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        }

        [Function(nameof(MessageRemoverFunction))]
        public async Task RemoveMessagesAsync([ActivityTrigger] MessageRemoverRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(MessageRemoverFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            if (request.RunId != Guid.Empty)
                await DeleteJobMessagesAsync(request);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(MessageRemoverFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
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

                if (message.MessageId.Contains(request.RunId.ToString(), StringComparison.InvariantCultureIgnoreCase))
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
