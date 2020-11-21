using Microsoft.Azure.ServiceBus;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Repositories.Mocks;

namespace Repositories.ServiceBusSubscriptions.Tests
{
    public class MockServiceBusSubscriptionsRepository : IServiceBusSubscriptionsRepository
    {
        public List<MockTopic> Topics { get; set; } = new List<MockTopic>();

        public async IAsyncEnumerable<Message> GetMessagesAsync(string topicName, string subscriptionName)
        {
            var messages = Topics.Single(t => t.Name == topicName).Subscriptions.Single(s => s.Name == subscriptionName).Messages;
            foreach (var message in await Task.FromResult(messages))
            {
                yield return message;
            }
        }
    }
}
