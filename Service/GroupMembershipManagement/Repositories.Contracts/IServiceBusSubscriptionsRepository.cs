// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ServiceBus;
using System.Collections.Generic;

namespace Repositories.Contracts
{
    public interface IServiceBusSubscriptionsRepository
    {
        IAsyncEnumerable<ServiceBusMessage> GetMessagesAsync(string topicName, string subscriptionName);
    }
}
