// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.ServiceBus;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Repositories.ServiceBusQueue
{
    public class ServiceBusQueueRepository : IServiceBusQueueRepository
    {
        private readonly IQueueClient _queueClient;

        public ServiceBusQueueRepository(IQueueClient queueClient)
        {
            _queueClient = queueClient ?? throw new ArgumentNullException(nameof(queueClient));
        }

        public async Task SendMessageAsync(ServiceBusMessage message)
        {
            var serviceBusmessage = new Message
            {
                Body = message.Body,
                MessageId = message.MessageId
            };

            if (message.UserProperties != null)
            {
                foreach (var property in message.UserProperties)
                {
                    serviceBusmessage.UserProperties.Add(property.Key, property.Value);
                }
            }

            await _queueClient.SendAsync(serviceBusmessage);
        }
    }
}
