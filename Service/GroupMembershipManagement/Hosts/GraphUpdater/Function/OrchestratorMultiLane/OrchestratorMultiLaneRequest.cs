// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ServiceBus;
using System;

namespace Hosts.GraphUpdater
{
    public class OrchestratorMultiLaneRequest
    {
        public Guid RunId { get; set; }
        public string TopicName { get; set; }
        public string SubscriptionName { get; set; }
        public GroupMembership GroupMembership { get; set; }
    }
}
