// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.ServiceBus;
using Services.Entities;

namespace Services.Contracts
{
    public interface IServiceBusMessageService
    {
        MessageInformation GetMessageProperties(Message message);
    }
}
