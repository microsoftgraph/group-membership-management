// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.Azure.ServiceBus;
using Repositories.Contracts;
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
		private readonly ILoggingRepository _logger;

		// at the moment, each instance of the function only handles one message a time for memory usage reasons
		// but this is good to have for later if things change
		private static readonly ConcurrentDictionary<string, List<GroupMembershipMessage>> _receivedMessages = new ConcurrentDictionary<string, List<GroupMembershipMessage>>();

		public SessionMessageCollector(IGraphUpdater graphUpdater, ILoggingRepository logger)
		{
			_graphUpdater = graphUpdater;
			_logger = logger;
		}

		public async Task<GroupMembershipMessageResponse> HandleNewMessageAsync(GroupMembershipMessage body, string messageSessionId)
		{
			_logger.SyncJobProperties = new Dictionary<string, string>()
			{ { "LockToken", body.LockToken }, { "RowKey", body.Body.SyncJobRowKey }, { "PartitionKey", body.Body.SyncJobPartitionKey }, { "TargetOfficeGroupId", body.Body.Destination.ObjectId.ToString() } };

			var receivedSoFar = _receivedMessages.AddOrUpdate(messageSessionId, new List<GroupMembershipMessage> { body }, (key, messages) => {
				messages.Add(body); return messages;
			});
			var handleNewMessageResponse = new GroupMembershipMessageResponse() { ShouldCompleteMessage = false };

			await _logger.LogMessageAsync(new LogMessage {
				RunId = body.Body.RunId,
				Message = $"Got a message in {nameof(SessionMessageCollector)}." +
				$"The message we just received has {body.Body.SourceMembers} users." +
				$"There are currently {_receivedMessages.Count} sessions in flight." +
				$"The current session, the one with ID {messageSessionId}, has {receivedSoFar.Count} messages with {receivedSoFar.Sum(x => x.Body.SourceMembers.Count)} users in total."
			});

			if (body.Body.IsLastMessage)
			{
				if (!_receivedMessages.TryRemove(messageSessionId, out var allReceivedMessages))
				{
					// someone else got to it first. shouldn't happen, but it's good to be prepared.
					return handleNewMessageResponse;
				}

				var received = GroupMembership.Merge(allReceivedMessages.Select(x => x.Body));

				await _logger.LogMessageAsync(new LogMessage
				{
					RunId = body.Body.RunId,
					Message = $"This message completed the session, so I'm going to sync {received.SourceMembers.Count} users."
				});

				await _graphUpdater.CalculateDifference(received);

				// If it succeeded, complete all the messages and close the session in the starter function
				handleNewMessageResponse.CompletedGroupMembershipMessages = allReceivedMessages;
				handleNewMessageResponse.ShouldCompleteMessage = true;

				return handleNewMessageResponse;
			}
			else
			{
				await _logger.LogMessageAsync(new LogMessage
				{
					RunId = body.Body.RunId,
					Message = "This is not the last message, so not doing anything right now."
				});
			}

			_logger.SyncJobProperties = null;
			return handleNewMessageResponse;

		}

	}
}
