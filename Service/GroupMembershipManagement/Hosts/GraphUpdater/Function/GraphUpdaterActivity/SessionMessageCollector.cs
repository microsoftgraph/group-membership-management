// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.ServiceBus;
using Microsoft.Azure.ServiceBus;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
	public class SessionMessageCollector
	{
		private readonly IGraphUpdater _graphUpdater;

		// at the moment, each instance of the function only handles one message a time for memory usage reasons
		// but this is good to have for later if things change
		private static readonly ConcurrentDictionary<string, List<GroupMembershipMessage>> _receivedMessages = new ConcurrentDictionary<string, List<GroupMembershipMessage>>();

		public SessionMessageCollector(IGraphUpdater graphUpdater)
		{
			_graphUpdater = graphUpdater;
		}

		public async Task<GroupMembershipMessageResponse> HandleNewMessageAsync(GroupMembershipMessage body, string messageSessionId)
		{
			_receivedMessages.AddOrUpdate(messageSessionId, new List<GroupMembershipMessage> { body }, (key, messages) => {
				messages.Add(body); return messages;
			});
			var handleNewMessageResponse = new GroupMembershipMessageResponse() { ShouldCompleteMessage = false };

			if (body.Body.IsLastMessage)
			{
				if (!_receivedMessages.TryRemove(messageSessionId, out var allReceivedMessages))
				{
					// someone else got to it first. shouldn't happen, but it's good to be prepared.
					return handleNewMessageResponse;
				}

				var received = GroupMembership.Merge(allReceivedMessages.Select(x => x.Body));

				await _graphUpdater.CalculateDifference(received);

				// If it succeeded, complete all the messages and close the session in the starter function
				handleNewMessageResponse.CompletedGroupMembershipMessages = allReceivedMessages;
				handleNewMessageResponse.ShouldCompleteMessage = true;

				return handleNewMessageResponse;
			}
			return handleNewMessageResponse;

		}

	}
}
