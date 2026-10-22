// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

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
        public List<string> IdentityTypes { get; set; }
        public bool WriteStaging { get; set; }
        public GroupMembershipReadKind? ReadKind { get; set; }

        /// <summary>
        /// The replay-safe time of this read attempt, used for the part-file, cache and delta-link names.
        /// Null in histories scheduled before this field existed; the activity then uses the current UTC time.
        /// </summary>
        public DateTime? FileTimestamp { get; set; }
    }
}