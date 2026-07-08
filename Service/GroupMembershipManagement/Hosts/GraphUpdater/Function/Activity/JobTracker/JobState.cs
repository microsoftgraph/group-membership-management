// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace GraphUpdater.Activity.JobTracker
{
    public class JobState
    {
        public bool? IsValidGroup { get; set; }
        public bool CompletionSent { get; set; }
        public int TotalMembersToAdd { get; set; }
        public int TotalMembersToRemove { get; set; }
        public int TotalMembersAdded { get; set; }
        public int TotalMembersRemoved { get; set; }
        public int TotalMembersToAddNotFound { get; set; }
        public int TotalMembersToAddAlreadyExist { get; set; }
        public int TotalMembersToRemoveNotFound { get; set; }
        public int MessagesProcessed { get; set; }

        /// <summary>
        /// Expected number of multi-lane messages for the run; set once (first-writer-wins).
        /// </summary>
        public int TotalMessageCount { get; set; }

        /// <summary>
        /// Single-writer completion claim: set true when every message has been processed, so exactly
        /// one orchestration finalizes the job and a recovered counter can't re-finalize it.
        /// </summary>
        public bool CompletionClaimed { get; set; }

        /// <summary>
        /// Message indices already folded into the totals; makes accumulation idempotent (each
        /// message counted exactly once).
        /// </summary>
        public HashSet<int> ProcessedMessageIndices { get; set; } = new HashSet<int>();
    }
}
