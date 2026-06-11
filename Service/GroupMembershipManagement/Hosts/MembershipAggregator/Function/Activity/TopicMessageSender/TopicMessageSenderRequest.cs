// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.MembershipAggregator
{
    public class TopicMessageSenderRequest
    {
        public required MembershipHttpRequest MembershipHttpRequest { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public required SyncJob SyncJob { get; init; }
    }
}
