// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.ServiceBus;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IServiceBusTopicsRepository
    {
        Task AddMessageAsync(SyncJob job);
        Task AddMessageAsync(ServiceBusMessage message);
    }
}
