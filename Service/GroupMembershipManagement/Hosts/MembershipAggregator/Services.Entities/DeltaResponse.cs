// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace MembershipAggregator.Services.Entities
{
    public class DeltaResponse
    {
        public int MembersToAddCount { get; set; }
        public int MembersToRemoveCount { get; set; }
        public MembershipDeltaStatus MembershipDeltaStatus { get; set; }
    }
}
