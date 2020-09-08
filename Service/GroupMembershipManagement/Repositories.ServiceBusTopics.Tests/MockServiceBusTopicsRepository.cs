using Entities;
using Microsoft.Azure.ServiceBus;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Tests.Repositories
{
    public class MockServiceBusTopicsRepository : IServiceBusTopicsRepository
    {
        public Dictionary<string, List<Message>> Subscriptions { get; private set; } = new Dictionary<string, List<Message>>();

        public async Task AddMessageAsync(SyncJob job)
        {
            var message = CreateMessage(job);
            var syncType = message.UserProperties["Type"].ToString();
            if (Subscriptions.ContainsKey(syncType))
            {
                Subscriptions[syncType].Add(message);
            }
            else
            {
                Subscriptions.Add(syncType, new List<Message> { message });
            }

            await Task.CompletedTask;
        }

        private Message CreateMessage(SyncJob job)
        {
            var body = JsonSerializer.Serialize(job);
            var message = new Message
            {
                Body = Encoding.UTF8.GetBytes(body)
            };

            message.UserProperties.Add("Type", job.Type);
            message.MessageId = $"{job.PartitionKey}_{job.RowKey}";

            return message;
        }
    }
}
