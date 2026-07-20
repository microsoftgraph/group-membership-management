// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;

namespace Hosts.MembershipAggregator
{
    public class MembershipSubOrchestratorRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public required Guid GroupId { get; init; }
        public required Dictionary<int, string> CompletedParts { get; init; }
        public required string DestinationPart { get; init; }
    }
}
