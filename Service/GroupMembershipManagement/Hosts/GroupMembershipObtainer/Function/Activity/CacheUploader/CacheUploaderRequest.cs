// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    public class CacheUploaderRequest
    {
        public Guid ObjectId { get; set; }
        public GroupMembershipFileResult MembershipFileResult { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }

        /// <summary>
        /// The replay-safe time of this read attempt, used for the part-file, cache and delta-link names.
        /// Null in histories scheduled before this field existed; the activity then uses the current UTC time.
        /// </summary>
        public DateTime? FileTimestamp { get; set; }
    }
}