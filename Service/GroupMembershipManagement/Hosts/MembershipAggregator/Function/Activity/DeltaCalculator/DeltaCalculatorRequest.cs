// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.MembershipAggregator
{
    public class DeltaCalculatorRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }

        /// <summary>
        /// Compressed serialized GroupMembership
        /// </summary>
        public required string SourceGroupMembership { get; init; }

        /// <summary>
        /// Compressed serialized GroupMembership
        /// </summary>
        public required string DestinationGroupMembership { get; init; }
        public required bool ReadFromBlobs { get; init; }
        public required string SourceMembershipFilePath { get; init; }
        public required string DestinationMembershipFilePath { get; init; }
    }
}

