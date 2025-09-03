// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Hosts.MembershipAggregator
{
    public class MembershipExtractionRequest
    {
        public List<string> CompletedParts { get; set; }
        public string DestinationPart { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
