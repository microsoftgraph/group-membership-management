// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ServiceBus;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface INotificationsQueueRepository
    {
        Task SendMessageAsync(ServiceBusMessage message);
    }
}
