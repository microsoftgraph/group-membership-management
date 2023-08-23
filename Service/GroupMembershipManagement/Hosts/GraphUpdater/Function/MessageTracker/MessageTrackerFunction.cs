// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageTrackerFunction : IMessageTracker
    {
        public Queue<string> MessageIds { get; set; } = new Queue<string>();

        public Task Add(string messageId)
        {
            MessageIds.Enqueue(messageId);
            return Task.CompletedTask;
        }

        public virtual Task Delete()
        {
            Entity.Current.DeleteState();
            return Task.CompletedTask;
        }

        public Task<string> GetNextMessageId()
        {
            if (MessageIds.Count == 0)
                return null;

            var messageId = MessageIds.Dequeue();
            return Task.FromResult(messageId);
        }

        [FunctionName(nameof(MessageTrackerFunction))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx) => ctx.DispatchAsync<MessageTrackerFunction>();
    }
}
