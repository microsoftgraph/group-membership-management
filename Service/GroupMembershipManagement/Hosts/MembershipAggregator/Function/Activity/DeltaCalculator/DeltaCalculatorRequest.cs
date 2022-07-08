// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.ServiceBus;
using System;

namespace Hosts.MembershipAggregator
{
    public class DeltaCalculatorRequest
    {
        public GroupMembership SourceGroupMembership { get; set; }
        public GroupMembership DestinationGroupMembership { get; set; }
        public bool ReadFromBlobs { get; set; }
        public string SourceMembershipFilePath { get; set; }
        public string DestinationMembershipFilePath { get; set; }
        public Guid? RunId { get; set; }
    }
}

