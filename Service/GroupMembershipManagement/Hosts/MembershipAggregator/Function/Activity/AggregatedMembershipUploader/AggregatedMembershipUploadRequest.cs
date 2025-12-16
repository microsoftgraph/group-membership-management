// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.MembershipAggregator
{
    public class AggregatedMembershipUploadRequest
    {
        public SyncJob SyncJob { get; set; }
        public Guid GroupId { get; set; }
        public string SourceMembershipFilePath { get; set; }
        public string CompressedMembersToAddJson { get; set; }
        public string CompressedMembersToRemoveJson { get; set; }
        public DateTime CurrentUtcDateTime { get; set; }
        public Guid RunId { get; set; }
    }
}
