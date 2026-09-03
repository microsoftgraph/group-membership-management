// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace MembershipAggregator.Services.Entities
{
    public sealed class MembershipDeltaSummary
    {
        public MembershipDeltaSummary(
            int sourceMemberCount,
            int destinationMemberCount,
            int membersToAddCount,
            int membersToRemoveCount)
        {
            SourceMemberCount = sourceMemberCount;
            DestinationMemberCount = destinationMemberCount;
            MembersToAddCount = membersToAddCount;
            MembersToRemoveCount = membersToRemoveCount;
        }

        public int SourceMemberCount { get; }

        public int DestinationMemberCount { get; }

        public int MembersToAddCount { get; }

        public int MembersToRemoveCount { get; }
    }
}
