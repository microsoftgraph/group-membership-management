// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.ServiceBus;
using System.Collections.Generic;


namespace Repositories.Mocks
{
    public class MockSubscription
    {
        public string Name { get; set; }
        public List<ServiceBusMessage> Messages { get; set; }
    }
}
