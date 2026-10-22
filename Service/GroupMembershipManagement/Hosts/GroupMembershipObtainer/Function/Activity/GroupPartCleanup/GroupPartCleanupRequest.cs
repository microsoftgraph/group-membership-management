// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Identifies the run-scoped blobs of one one-type incremental read of a part.
    /// </summary>
    public sealed class GroupPartCleanupRequest
    {
        public SyncJob SyncJob { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public Guid TargetGroupId { get; set; }

        /// <summary>
        /// The part file the abandoned attempt may have committed, deleted by exact name before a replacement read.
        /// Null when no attempt is abandoned.
        /// </summary>
        public string AbandonedPartFilePath { get; set; }

        /// <summary>
        /// <see langword="true"/> when a replacement read follows: the merge-prefix page uploads of the abandoned
        /// read are deleted first and the prefix must then list empty, so the replacement read never merges them.
        /// Any failure returns <see cref="GroupPartCleanupStatus.Incomplete"/> and the replacement read is not started.
        /// </summary>
        public bool BeforeReplacementRead { get; set; }
    }
}
