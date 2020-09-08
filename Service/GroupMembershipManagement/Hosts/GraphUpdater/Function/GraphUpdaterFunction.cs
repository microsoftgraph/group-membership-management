using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities.ServiceBus;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Hosts.GraphUpdater
{
    public class GraphUpdaterFunction
    {
		private readonly SessionMessageCollector _messageCollector;

		public GraphUpdaterFunction(SessionMessageCollector messageCollector)
		{
            _messageCollector = messageCollector;
		}

		[FunctionName(nameof(GraphUpdater))]
        public async Task Run([ServiceBusTrigger("%membershipQueueName%", Connection = "differenceQueueConnection", IsSessionsEnabled = true)] Message message, IMessageSession messageSession)
        {
            var body = new GroupMembershipMessage {
				Body =	JsonConvert.DeserializeObject<GroupMembership>(Encoding.UTF8.GetString(message.Body)),
				LockToken = message.SystemProperties.LockToken
			};

			await _messageCollector.HandleNewMessage(body, messageSession);
		}

    }

	public class GroupMembershipMessage 
	{
		public GroupMembership Body { get; set; }
		public string LockToken { get; set; }
	}
}
