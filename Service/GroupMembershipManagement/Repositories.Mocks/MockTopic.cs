using System.Collections.Generic;

namespace Repositories.Mocks
{
    public class MockTopic
    {
        public string Name { get; set; }
        public List<MockSubscription> Subscriptions { get; set; }
    }
}
