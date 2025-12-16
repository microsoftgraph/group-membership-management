// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.ServiceBus;
using System.Text.Json.Serialization;

namespace Hosts.MembershipAggregator
{
    public class MembershipExtractionResponse
    {
        public bool IsSuccessful { get; set; }
        public string ErrorMessage { get; set; }
        [JsonIgnore]
        public GroupMembership SourceMembership { get; set; }
        [JsonIgnore]
        public GroupMembership DestinationMembership { get; set; }
        public string SourceMembershipFilePath { get; set; }
        public string DestinationMembershipFilePath { get; set; }
        public int SourceMemberCount { get; set; }
        public int DestinationMemberCount { get; set; }
    }
}
