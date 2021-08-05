// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.ServiceBus;
using Services.Contracts;
using Services.Entities;

namespace Services
{
    public class ServiceBusMessageService : IServiceBusMessageService
    {
        public MessageInformation GetMessageProperties(Message message)
        {
            return new MessageInformation
            {
                Body = message.Body,
                LockToken = message.SystemProperties.LockToken,
                SessionId = message.SessionId
            };
        }
    }
}
