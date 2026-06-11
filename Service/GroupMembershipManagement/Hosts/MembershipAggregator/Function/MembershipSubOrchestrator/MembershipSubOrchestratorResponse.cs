// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Services.Entities;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorResponse
    {
        public string FilePath { get; set; }
        public MembershipDeltaStatus MembershipDeltaStatus { get; set; }
        public int ProjectedMemberCount { get; set; }
        public int MembersToBeAdded { get; set; }
        public int MembersToBeRemoved { get; set; }
    }
}
