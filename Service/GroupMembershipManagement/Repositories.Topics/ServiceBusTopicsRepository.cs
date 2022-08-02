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
using System;

namespace Repositories.ServiceBusTopics
{
    public class ServiceBusTopicsRepository : IServiceBusTopicsRepository
    {
        private ITopicClient _topicClient;

        public ServiceBusTopicsRepository(ITopicClient topicClient)
        {
            _topicClient = topicClient ?? throw new ArgumentNullException(nameof(topicClient));
        }

        public async Task AddMessageAsync(SyncJob job)
        {
            var index = 1;
            var queries = JArray.Parse(job.Query);
            var queryTypes = queries.Select(x => new
            {
                type = (string)x["type"],
                exclusionary = x["exclusionary"] != null ? (bool)x["exclusionary"] : false
            }).ToList();

            // + 1 to include destination group
            var totalParts = queryTypes.Count + 1;

            foreach (var type in queryTypes)
            {
                var sourceGroupMessage = CreateMessage(job);
                sourceGroupMessage.UserProperties.Add("Type", type.type);
                sourceGroupMessage.UserProperties.Add("Exclusionary", type.exclusionary);
                sourceGroupMessage.UserProperties.Add("TotalParts", totalParts);
                sourceGroupMessage.UserProperties.Add("CurrentPart", index);
                sourceGroupMessage.MessageId += $"_{index++}";
                await _topicClient.SendAsync(sourceGroupMessage);
            }

            var destinationGroupMessage = CreateMessage(job);
            destinationGroupMessage.UserProperties.Add("Type", "SecurityGroup");
            destinationGroupMessage.UserProperties.Add("TotalParts", totalParts);
            destinationGroupMessage.UserProperties.Add("CurrentPart", index);
            destinationGroupMessage.UserProperties.Add("IsDestinationPart", true);
            destinationGroupMessage.MessageId += $"_{index}";
            await _topicClient.SendAsync(destinationGroupMessage);
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
