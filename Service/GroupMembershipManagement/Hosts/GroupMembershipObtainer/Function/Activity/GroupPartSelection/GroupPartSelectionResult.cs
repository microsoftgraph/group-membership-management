// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// The finalize result: the published part file and the counts computed from the run's own snapshot.
    /// </summary>
    public sealed class GroupPartSelectionResult
    {
        public SelectionOutcome Outcome { get; set; }
        public string FilePath { get; set; }
        public int MemberCount { get; set; }
        public int UserCount { get; set; }
        public int AgentCount { get; set; }
        public int NotIdentifiedCount { get; set; }
        public int LookupCount { get; set; }
    }

    /// <summary>
    /// The prepare result, always taken from the winning manifest. An empty <see cref="UnknownSegments"/>
    /// means that no lookup is needed.
    /// </summary>
    public sealed class GroupPartPrepareResult
    {
        public SelectionOutcome Outcome { get; set; }
        public Guid ExecutionId { get; set; }
        public int StagingCount { get; set; }
        public int SegmentCount { get; set; }
        public List<int> UnknownSegments { get; set; } = new List<int>();
    }

    /// <summary>
    /// The summary of one segment's receipt, newly written or reused.
    /// </summary>
    public sealed class GroupPartSegmentResult
    {
        public SelectionOutcome Outcome { get; set; }
        public int SegmentIndex { get; set; }
        public int LookupCount { get; set; }
        public int IdentifiedCount { get; set; }
        public int NotIdentifiedCount { get; set; }
    }

    /// <summary>
    /// The best-effort result of merging this read's identified types into the shared record.
    /// </summary>
    public sealed class GroupPartRecordTypesResult
    {
        /// <summary><see langword="true"/> when the record was rewritten.</summary>
        public bool Written { get; set; }

        /// <summary>The conditional write attempts made (0 when the record was unchanged and fresh).</summary>
        public int Attempts { get; set; }

        /// <summary><see langword="true"/> when every attempt lost its ETag condition.</summary>
        public bool Exhausted { get; set; }

        /// <summary><see langword="true"/> when the step failed for another reason; it is logged and ignored.</summary>
        public bool Failed { get; set; }
    }
}
