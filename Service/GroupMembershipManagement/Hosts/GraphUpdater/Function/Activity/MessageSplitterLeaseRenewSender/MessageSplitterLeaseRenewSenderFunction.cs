// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models.ServiceBus;
using Repositories.Contracts.Helpers;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageSplitterLeaseRenewSenderFunction
    {
        private readonly ServiceBusSender _messageSplitterTopicSender;
        private readonly ILogger<MessageSplitterLeaseRenewSenderFunction> _logger;

        public MessageSplitterLeaseRenewSenderFunction(
            [FromKeyedServices("messageSplitterTopicSender")] ServiceBusSender messageSplitterTopicSender,
            ILogger<MessageSplitterLeaseRenewSenderFunction> logger)
        {
            _messageSplitterTopicSender = messageSplitterTopicSender ?? throw new ArgumentNullException(nameof(messageSplitterTopicSender));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(MessageSplitterLeaseRenewSenderFunction))]
        public async Task RunAsync([ActivityTrigger] MessageSplitterLeaseRenewSignal request)
        {
            using var scope = _logger.BeginRunIdScope(request.RunId);
            _logger.SendingLeaseRenew();

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
