// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ServiceBus;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IServiceBusQueueRepository
    {
        Task SendMessageAsync(ServiceBusMessage message);
    }
}
