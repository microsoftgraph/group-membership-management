// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageSplitterLeaseRenewSenderFunction
    {
        private readonly ServiceBusSender _messageSplitterTopicSender;
        private readonly ILoggingRepository _loggingRepository;

        public MessageSplitterLeaseRenewSenderFunction(
            [FromKeyedServices("messageSplitterTopicSender")] ServiceBusSender messageSplitterTopicSender,
            ILoggingRepository loggingRepository)
        {
            _messageSplitterTopicSender = messageSplitterTopicSender ?? throw new ArgumentNullException(nameof(messageSplitterTopicSender));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(MessageSplitterLeaseRenewSenderFunction))]
        public async Task RunAsync([ActivityTrigger] MessageSplitterLeaseRenewSignal request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(MessageSplitterLeaseRenewSenderFunction)} sending lease renew",
                RunId = request.RunId
            }, VerbosityLevel.DEBUG);

            var message = new Azure.Messaging.ServiceBus.ServiceBusMessage(BinaryData.FromString(JsonSerializer.Serialize(request)))
            {
                // Must be unique per heartbeat (duplicate detection may be enabled).
                MessageId = $"lease_renew_{request.RunId}_{request.LaneSize}_{Guid.NewGuid()}"
            };

            message.ApplicationProperties["MessageType"] = $"lease_renew_{request.LaneSize.ToLowerInvariant()}";
            message.ApplicationProperties["RunId"] = request.RunId.ToString();

            await _messageSplitterTopicSender.SendMessageAsync(message);
        }
    }
}
