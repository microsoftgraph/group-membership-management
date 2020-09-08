// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.ServiceBus;
using Repositories.Contracts;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;

namespace Repositories.ServiceBusTopics
{
    public class ServiceBusTopicsRepository : IServiceBusTopicsRepository
    {
        private TopicClient _topicClient;

        public ServiceBusTopicsRepository(string connectionString, string entityPath)
        {
            _topicClient = new TopicClient(connectionString, entityPath);
        }

        public async Task AddMessageAsync(SyncJob job)
        {
            await _topicClient.SendAsync(CreateMessage(job));
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

