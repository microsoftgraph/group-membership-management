// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageEntity : IMessageEntity
    {
        public MembershipHttpRequest Message { get; set; }
        public Task Save(MembershipHttpRequest message)
        {
            Message = message;
            return Task.CompletedTask;
        }

        public Task<MembershipHttpRequest> Get()
        {
            return Task.FromResult(Message);
        }

        public virtual Task Delete()
        {
            Entity.Current.DeleteState();
            return Task.CompletedTask;
        }

        [FunctionName(nameof(MessageEntity))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx) => ctx.DispatchAsync<MessageEntity>();
    }
}
