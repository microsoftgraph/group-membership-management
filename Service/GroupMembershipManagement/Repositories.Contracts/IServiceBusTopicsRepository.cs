// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.ServiceBus;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IServiceBusTopicsRepository
    {
        Task AddMessageAsync(SyncJob job);
        Task AddMessageAsync(ServiceBusMessage message);
        Task AddMessagesAsync(IEnumerable<ServiceBusMessage> messages);
    }
}
