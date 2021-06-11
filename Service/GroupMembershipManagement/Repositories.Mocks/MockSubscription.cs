// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.ServiceBus;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Repositories.Mocks
{
	[ExcludeFromCodeCoverage]
    public class MockSubscription
    {
        public string Name { get; set; }
        public List<Message> Messages { get; set; }
    }
}
