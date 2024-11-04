// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    public class MembershipHttpRequest
    {
        public string FilePath { get; set; }
        public SyncJob SyncJob { get; set; }
        public int? ProjectedMemberCount { get; set; }
        public int MembersToBeAdded { get; set; }
        public int MembersToBeRemoved { get; set; }
        public int MembersToBeUpdated => MembersToBeAdded + MembersToBeRemoved;
    }
}
