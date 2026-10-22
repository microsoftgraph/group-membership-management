// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaUserReaderRequest
    {
        public Guid ObjectId { get; set; }
        public Guid TargetGroupId { get; set; }
        public int CurrentPart { get; set; }
        public int PageCount { get; set; }
        public int TotalParts { get; set; }
        public SyncJob SyncJob { get; set; }
        public List<string> IdentityTypes { get; set; }
        public GroupMembershipReadKind? ReadKind { get; set; }

        /// <summary>
        /// Whether this read is the job's destination part. Readers set <c>SourceGroup</c> on every member unless
        /// this is true (FR-010), including for a source part whose group equals the destination. Null in requests
        /// recorded before the field existed; readers then fall back to comparing the source and target groups.
        /// </summary>
        public bool? IsDestinationPart { get; set; }
    }
}