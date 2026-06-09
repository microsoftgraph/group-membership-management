// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Entities;

namespace Hosts.GraphUpdater
{
    public class MessageRemoverRequest : GraphUpdaterRequestBase
    {
        public string TopicName { get; set; }
        public string SubscriptionName { get; set; }
    }
}
