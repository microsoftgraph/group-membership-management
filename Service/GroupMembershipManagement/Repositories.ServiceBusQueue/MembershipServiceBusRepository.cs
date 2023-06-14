// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Models.ServiceBus;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.ServiceBusQueue
{
    public class MembershipServiceBusRepository : IMembershipServiceBusRepository
    {
        private ServiceBusSender _serviceBusSender;

        public MembershipServiceBusRepository(ServiceBusSender serviceBusSender)
        {
            _serviceBusSender = serviceBusSender ?? throw new ArgumentNullException(nameof(serviceBusSender));
        }

        public async Task SendMembership(GroupMembership groupMembership, string sentFrom = "")
        {
            if (groupMembership.SyncJobPartitionKey == null) { throw new ArgumentNullException("SyncJobPartitionKey must be set."); }
            if (groupMembership.SyncJobRowKey == null) { throw new ArgumentNullException("SyncJobRowKey must be set."); }

            foreach (var message in groupMembership.Split().Select(x => new Azure.Messaging.ServiceBus.ServiceBusMessage
            {
                Body = new BinaryData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(x))),
                SessionId = groupMembership.RunId.ToString(),
                ContentType = "application/json"
            }))
            {
                await _serviceBusSender.SendMessageAsync(message);
            }
        }
    }
}
