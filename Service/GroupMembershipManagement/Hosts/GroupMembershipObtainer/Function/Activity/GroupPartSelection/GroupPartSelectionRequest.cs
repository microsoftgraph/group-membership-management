// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Input to the prepare, classify, finalize and record-types activities of one one-type incremental read.
    /// </summary>
    public sealed class GroupPartSelectionRequest
    {
        public SyncJob SyncJob { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public Guid SourceGroupId { get; set; }
        public Guid TargetGroupId { get; set; }
        public List<string> IdentityTypes { get; set; }
        public GroupMembershipReadKind? ReadKind { get; set; }

        /// <summary>The part-file path the finalize step publishes, named from the read attempt's timestamp.</summary>
        public string PartFilePath { get; set; }

        /// <summary>The unfiltered, write-once staging blob of the read.</summary>
        public string StagingPath { get; set; }

        /// <summary>The segment to classify. Set only for the classify activity.</summary>
        public int? SegmentIndex { get; set; }
    }
}
