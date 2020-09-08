// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.ServiceBus;
using System.Collections.Generic;

namespace Repositories.Contracts
{
    public interface IServiceBusSubscriptionsRepository
    {
        IAsyncEnumerable<Message> GetMessagesAsync(string topicName, string subscriptionName);
    }
}

