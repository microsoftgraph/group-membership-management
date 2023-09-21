// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class MessageTrackerFunction : IMessageTracker
    {
        public const string EntityName = $"{nameof(MessageTrackerFunction)}";
        public const string EntityKey = $"{nameof(MessageTrackerFunction)}_TCU";

        public Queue<string> MessageIds { get; set; } = new Queue<string>();

        public Task AddAsync(string messageId)
        {
            MessageIds.Enqueue(messageId);
            return Task.CompletedTask;
        }

        public virtual Task DeleteAsync()
        {
            Entity.Current.DeleteState();
            return Task.CompletedTask;
        }

        public Task<string> GetNextMessageIdAsync()
        {
            if (MessageIds.Count == 0)
                return null;

            var messageId = MessageIds.Dequeue();
            return Task.FromResult(messageId);
        }

        public Task<int> GetMessageCountAsync()
        {
            return Task.FromResult(MessageIds.Count);
        }

        [FunctionName(nameof(MessageTrackerFunction))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx) => ctx.DispatchAsync<MessageTrackerFunction>();
    }
}
