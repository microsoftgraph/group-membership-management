// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class ProcessCachedAndDeltaUsersRequest
    {
        public SyncJob SyncJob { get; set; }
        public Guid SourceGroupId { get; set; }
        public Guid TargetGroupId { get; set; }
        public string CacheFilePath { get; set; }
        public string DeltaUrl { get; set; }
        public bool Exclusionary { get; set; }
        public int CountOfUsersFromAADGroup { get; set; }
        public bool TrackCachedUsersEvent { get; set; } = true;
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
    }
}