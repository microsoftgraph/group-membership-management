// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.ServiceBus;

namespace Hosts.MembershipAggregator
{
    public class DeltaCalculatorRequest
    {
        public GroupMembership SourceGroupMembership { get; set; }
        public GroupMembership DestinationGroupMembership { get; set; }
    }
}
