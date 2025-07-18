// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Models
{
    public class MembershipHttpRequest
    {
        public required string FilePath { get; init; }
        public required SyncJob SyncJob { get; init; }
        public required int ProjectedMemberCount { get; init; }
        public required int MembersToBeAdded { get; init; }
        public required int MembersToBeRemoved { get; init; }
        public required Guid GroupId { get; init; }
        public int MembersToBeUpdated => MembersToBeAdded + MembersToBeRemoved;
    }
}
