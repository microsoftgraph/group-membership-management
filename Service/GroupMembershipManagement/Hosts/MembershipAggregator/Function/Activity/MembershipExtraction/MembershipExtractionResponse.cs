// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.ServiceBus;

namespace Hosts.MembershipAggregator
{
    public class MembershipExtractionResponse
    {
        public bool IsSuccessful { get; set; }
        public string ErrorMessage { get; set; }
        public GroupMembership SourceMembership { get; set; }
        public GroupMembership DestinationMembership { get; set; }
    }
}
