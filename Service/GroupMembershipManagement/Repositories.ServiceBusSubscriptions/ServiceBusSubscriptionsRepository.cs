// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Messaging.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Repositories.ServiceBusSubscriptions
{
    public class ServiceBusSubscriptionsRepository : IServiceBusSubscriptionsRepository
    {
        private readonly ServiceBusReceiver _serviceBusReceiver;

        public ServiceBusSubscriptionsRepository(ServiceBusReceiver serviceBusReceiver)
        {
            _serviceBusReceiver = serviceBusReceiver ?? throw new ArgumentNullException(nameof(serviceBusReceiver));
        }

        public async IAsyncEnumerable<Models.ServiceBus.ServiceBusMessage> GetMessagesAsync(string topicName, string subscriptionName)
        {
            IReadOnlyList<ServiceBusReceivedMessage> messages = null;

            do
            {
                messages = await _serviceBusReceiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(5));

                foreach (var message in (messages ?? Enumerable.Empty<ServiceBusReceivedMessage>()))
                {
                    var newMessage = new Models.ServiceBus.ServiceBusMessage
                    {
                        Body = message.Body.ToArray(),
                        MessageId = message.MessageId,
                    };

                    if (message.ApplicationProperties != null)
                    {
                        foreach (var keyValuePair in message.ApplicationProperties)
                        {
                            newMessage.ApplicationProperties.Add(keyValuePair.Key, keyValuePair.Value);
                        }
                    }

                    yield return newMessage;

                    await _serviceBusReceiver.CompleteMessageAsync(message);
                }

            } while (messages?.Any() ?? false);

            await _serviceBusReceiver.CloseAsync();
        }
    }
}
