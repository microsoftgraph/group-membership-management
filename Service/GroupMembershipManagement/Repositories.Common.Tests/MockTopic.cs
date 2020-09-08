// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace Tests.Repositories.Common
{
    public class MockTopic
    {
        public string Name { get; set; }
        public List<MockSubscription> Subscriptions { get; set; }
    }
}

