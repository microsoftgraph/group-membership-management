// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;

namespace Hosts.MembershipAggregator
{
    public class JobState
    {
        public List<string> CompletedParts { get; set; } = new List<string>();
        public string DestinationPart { get; set; }
        public int TotalParts { get; set; }
    }
}
