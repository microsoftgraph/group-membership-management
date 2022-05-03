// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.ServiceBus;
using Repositories.Contracts;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

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
            var index = 1;
            var queries = JArray.Parse(job.Query);
            var queryTypes = queries.SelectTokens("$..type")
                                    .Select(x => x.Value<string>())
                                    .ToList();

            foreach(var type in queryTypes)
            {
                var message = CreateMessage(job);
                message.UserProperties.Add("Type", type);
                message.UserProperties.Add("TotalParts", queryTypes.Count);
                message.UserProperties.Add("CurrentPart", index);
                message.MessageId += $"_{index++}";
                await _topicClient.SendAsync(message);
            }
        }

        private Message CreateMessage(SyncJob job)
        {
            var body = JsonSerializer.Serialize(job);
            var message = new Message
            {
                Body = Encoding.UTF8.GetBytes(body)
            };

            message.MessageId = $"{job.PartitionKey}_{job.RowKey}_{job.RunId}";

            return message;
        }
    }
}
