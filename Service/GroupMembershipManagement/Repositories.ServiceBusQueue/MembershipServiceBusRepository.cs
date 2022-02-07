// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Primitives;
using System;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System.Threading.Tasks;
using Entities.ServiceBus;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using System.Text;

namespace Repositories.ServiceBusQueue
{
	public class MembershipServiceBusRepository : IMembershipServiceBusRepository
	{
		private readonly QueueClient _queueClient;

		public MembershipServiceBusRepository(string serviceBusNamespacePrefix, string queueName)
		{
            var msiTokenProvider = TokenProvider.CreateManagedIdentityTokenProvider();
			_queueClient = new QueueClient($"https://{serviceBusNamespacePrefix}.servicebus.windows.net", queueName, msiTokenProvider);
		}

		public async Task SendMembership(GroupMembership groupMembership, string sentFrom = "")
		{
			if (groupMembership.SyncJobPartitionKey == null) { throw new ArgumentNullException("SyncJobPartitionKey must be set."); }
			if (groupMembership.SyncJobRowKey == null) { throw new ArgumentNullException("SyncJobRowKey must be set."); }

			foreach (var message in groupMembership.Split().Select(x => new Message
			{
				Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(x)),
				SessionId = groupMembership.RunId.ToString(),
				ContentType = "application/json",
				Label = sentFrom
			}))
			{
				await _queueClient.SendAsync(message);
			}
		}
	}
}
