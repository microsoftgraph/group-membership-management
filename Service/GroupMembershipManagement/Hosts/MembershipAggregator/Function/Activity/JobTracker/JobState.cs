// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;

namespace Hosts.MembershipAggregator
{
    public class JobState
    {
        public int TotalParts { get; set; }

        // PartNumber and Blob path.
        public Dictionary<int, string> CompletedParts { get; set; } = new Dictionary<int, string>();

        // Path of the part marked IsDestinationPart=true (or null if none yet).
        // The destination is also one of the entries in CompletedParts.
        public string DestinationPart { get; set; }

        // Single-writer claim: set true the first time RegisterPartAndCheckComplete
        // observes all parts present. Prevents two racing orchestrators from both
        // proceeding to MembershipSubOrchestratorFunction / TopicMessageSenderFunction.
        public bool CompletionClaimed { get; set; }
    }
}

