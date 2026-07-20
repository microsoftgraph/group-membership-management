// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public interface IJobTracker
    {
        // Atomic register-and-check.
        Task<JobTrackerCompletionResult> RegisterPartAndCheckComplete(JobTrackerRegistration registration);
    }

    public class JobTrackerRegistration
    {
        public int PartNumber { get; set; }
        public int TotalParts { get; set; }
        public string FilePath { get; set; }
        public bool IsDestinationPart { get; set; }
    }

    public class JobTrackerCompletionResult
    {
        public bool IsComplete { get; set; }
        public int CompletedCount { get; set; }
        public int TotalParts { get; set; }
        public Dictionary<int, string> CompletedParts { get; set; } = new Dictionary<int, string>();
        public string DestinationPart { get; set; }
    }
}
