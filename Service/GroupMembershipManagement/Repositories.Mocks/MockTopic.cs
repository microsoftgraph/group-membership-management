// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Repositories.Mocks
{
	[ExcludeFromCodeCoverage]
    public class MockTopic
    {
        public string Name { get; set; }
        public List<MockSubscription> Subscriptions { get; set; }
    }
}
