// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.GroupMembershipObtainer
{
    public class JobStatusUpdaterRequest
    {
        public SyncJob SyncJob { get; set; }
        public SyncStatus Status { get; set; }
        public int? BeforeSyncUserCount { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
    }
}