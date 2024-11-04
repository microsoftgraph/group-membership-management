// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.MessageSplitter
{
    public class TopicMessageSenderRequest
    {
        public int MessageSize { get; set; }
        public MembershipHttpRequest MembershipRequest { get; set; }
        public string SubscriptionName { get; set; }
        public string LaneSize { get; set; }
        public int InstanceToUse { get; set; }

    }
}
