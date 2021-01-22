// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.ServiceBus.Primitives;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Repositories.ServiceBusSubscriptions
{
    public class ServiceBusSubscriptionsRepository : IServiceBusSubscriptionsRepository
    {
        private readonly string _serviceBusNamespace;
        public ServiceBusSubscriptionsRepository(string serviceBusNamespace)
        {
            _serviceBusNamespace = serviceBusNamespace;
        }

        public async IAsyncEnumerable<Message> GetMessagesAsync(string topicName, string subscriptionName)
        {
            var tokenProvider = TokenProvider.CreateManagedIdentityTokenProvider();
            var entityPath = EntityNameHelper.FormatSubscriptionPath(topicName, subscriptionName);
            var receiver = new MessageReceiver($"sb://{_serviceBusNamespace}.servicebus.windows.net/", entityPath, tokenProvider);
            IList<Message> messages = null;

            do
            {
                messages = await receiver.ReceiveAsync(100, TimeSpan.FromSeconds(5));

                foreach (var message in (messages ?? Enumerable.Empty<Message>()))
                {
                    yield return message;
                    await receiver.CompleteAsync(message.SystemProperties.LockToken);
                }

            } while (messages?.Any() ?? false);

            await receiver.CloseAsync();
        }
    }
}
