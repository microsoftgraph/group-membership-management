// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Message = Azure.Messaging.ServiceBus.ServiceBusMessage;
using MessageDTO = Models.ServiceBus.ServiceBusMessage;

namespace Repositories.ServiceBusTopics
{
    public class ServiceBusTopicsRepository : IServiceBusTopicsRepository
    {
        private ServiceBusSender _serviceBusSender;

        public ServiceBusTopicsRepository(ServiceBusSender serviceBusSender)
        {
            _serviceBusSender = serviceBusSender ?? throw new ArgumentNullException(nameof(serviceBusSender));
        }

        public async Task AddMessageAsync(SyncJob job)
        {
            var index = 1;

            // Parse the JSON array
            var queries = JsonNode.Parse(job.Query).AsArray();

            // Select the query types
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
                sourceGroupMessage.ApplicationProperties.Add("Type", type.type);
                sourceGroupMessage.ApplicationProperties.Add("Exclusionary", type.exclusionary);
                sourceGroupMessage.ApplicationProperties.Add("TotalParts", totalParts);
                sourceGroupMessage.ApplicationProperties.Add("CurrentPart", index);
                sourceGroupMessage.MessageId += $"_{index++}";
                await _serviceBusSender.SendMessageAsync(sourceGroupMessage);
            }

            var destinationType = job.MembershipType;

            var destinationGroupMessage = CreateMessage(job);
            destinationGroupMessage.ApplicationProperties.Add("Type", destinationType);
            destinationGroupMessage.ApplicationProperties.Add("TotalParts", totalParts);
            destinationGroupMessage.ApplicationProperties.Add("CurrentPart", index);
            destinationGroupMessage.ApplicationProperties.Add("IsDestinationPart", true);
            destinationGroupMessage.MessageId += $"_{index}";
            await _serviceBusSender.SendMessageAsync(destinationGroupMessage);
        }

        public async Task AddMessageAsync(MessageDTO message)
        {
            var serviceBusmessage = new Message
            {
                Body = new BinaryData(message.Body),
                MessageId = message.MessageId
            };

            if (message.ApplicationProperties != null)
            {
                foreach (var property in message.ApplicationProperties)
                {
                    serviceBusmessage.ApplicationProperties.Add(property.Key, property.Value);
                }
            }

            await _serviceBusSender.SendMessageAsync(serviceBusmessage);
        }

        public async Task AddMessagesAsync(IEnumerable<MessageDTO> messages)
        {
            var batch = await _serviceBusSender.CreateMessageBatchAsync();

            foreach (var message in messages)
            {
                var serviceBusmessage = new Message
                {
                    Body = new BinaryData(message.Body),
                    MessageId = message.MessageId
                };

                if (message.ApplicationProperties != null)
                {
                    foreach (var property in message.ApplicationProperties)
                    {
                        serviceBusmessage.ApplicationProperties.Add(property.Key, property.Value);
                    }
                }

                if (!batch.TryAddMessage(serviceBusmessage))
                {
                    await _serviceBusSender.SendMessagesAsync(batch);
                    batch = await _serviceBusSender.CreateMessageBatchAsync();
                    batch.TryAddMessage(serviceBusmessage);
                }
            }

            if (batch.Count > 0)
            {
                await _serviceBusSender.SendMessagesAsync(batch);
            }
        }

        private Message CreateMessage(SyncJob job)
        {
            var body = JsonSerializer.Serialize(job);
            var message = new Message
            {
                Body = new BinaryData(Encoding.UTF8.GetBytes(body))
            };

            message.MessageId = $"{job.PartitionKey}_{job.RowKey}_{job.RunId}";

            return message;
        }
    }
}
