// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Hosts.GroupMembershipObtainer
{
    public class ProcessCachedAndDeltaUsersResponse
    {
        public string MembershipFilePath { get; set; }
        public bool CacheMatchesGroupCount { get; set; }
        public int CacheCount { get; set; }
    }
}
