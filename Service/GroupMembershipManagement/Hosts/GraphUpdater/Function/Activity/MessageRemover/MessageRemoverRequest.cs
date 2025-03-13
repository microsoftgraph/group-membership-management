// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.GraphUpdater
{
    public class MessageRemoverRequest
    {
        public Guid RunId { get; set; }
        public string TopicName { get; set; }
        public string SubscriptionName { get; set; }
    }
}
