// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Repositories.ServiceBusQueue
{
    public class ServiceBusQueueRepository : IServiceBusQueueRepository
    {
        private ServiceBusSender _serviceBusSender;

        public ServiceBusQueueRepository(ServiceBusSender serviceBusSender)
        {
            _serviceBusSender = serviceBusSender ?? throw new ArgumentNullException(nameof(serviceBusSender));
        }

        public async Task SendMessageAsync(Models.ServiceBus.ServiceBusMessage message)
        {
            var serviceBusmessage = new ServiceBusMessage
            {
                Body = new BinaryData(message.Body),
                MessageId = message.MessageId
            };

            if (message.UserProperties != null)
            {
                foreach (var property in message.UserProperties)
                {
                    serviceBusmessage.ApplicationProperties.Add(property.Key, property.Value);
                }
            }

            await _serviceBusSender.SendMessageAsync(serviceBusmessage);
        }
    }
}
