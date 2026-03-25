// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.MembershipAggregator
{
    public class AggregatedMembershipUploadRequest
    {
        public required SyncJob SyncJob { get; set; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public Guid GroupId { get; set; }
        public required string SourceMembershipFilePath { get; set; }
        public required string CompressedMembersToAddJson { get; set; }
        public required string CompressedMembersToRemoveJson { get; set; }
        public DateTime CurrentUtcDateTime { get; set; }
    }
}
