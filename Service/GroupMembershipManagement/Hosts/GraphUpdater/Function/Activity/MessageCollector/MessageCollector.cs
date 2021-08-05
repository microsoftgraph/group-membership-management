// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageCollector
    {
        private readonly ILoggingRepository _loggingRepository;

        // at the moment, each instance of the function only handles one message a time for memory usage reasons
        // but this is good to have for later if things change
        private static readonly ConcurrentDictionary<string, List<GroupMembershipMessage>> _receivedMessages = new ConcurrentDictionary<string, List<GroupMembershipMessage>>();

        public MessageCollector(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task<GroupMembershipMessageResponse> HandleNewMessageAsync(GroupMembershipMessage body, string messageSessionId)
        {
            _loggingRepository.SyncJobProperties = new Dictionary<string, string>()
            { { "LockToken", body.LockToken }, { "RowKey", body.Body.SyncJobRowKey }, { "PartitionKey", body.Body.SyncJobPartitionKey }, { "TargetOfficeGroupId", body.Body.Destination.ObjectId.ToString() } };

            var receivedSoFar = _receivedMessages.AddOrUpdate(messageSessionId, new List<GroupMembershipMessage> { body }, (key, messages) =>
            {
                messages.Add(body); return messages;
            });

            var handleNewMessageResponse = new GroupMembershipMessageResponse() { ShouldCompleteMessage = false };

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = body.Body.RunId,
                Message = $"Got a message in {nameof(MessageCollector)}. " +
                $"The message we just received has {body.Body.SourceMembers.Count} users and is {(body.Body.IsLastMessage ? "" : "not ")}the last message in its session. " +
                $"There are currently {_receivedMessages.Count} sessions in flight. " +
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

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = body.Body.RunId,
                    Message = $"This message completed the session, so I'm going to sync {received.SourceMembers.Count} users."
                });

                handleNewMessageResponse.CompletedGroupMembershipMessages = allReceivedMessages;
                handleNewMessageResponse.ShouldCompleteMessage = true;

                return handleNewMessageResponse;
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = body.Body.RunId,
                    Message = "This is not the last message, so not doing anything right now."
                });
            }

            _loggingRepository.SyncJobProperties = null;
            return handleNewMessageResponse;
        }
    }
}
