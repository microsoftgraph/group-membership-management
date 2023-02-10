// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;

namespace Hosts.MembershipAggregator
{
    public class JobStatusUpdaterRequest
    {
        public SyncJob SyncJob { get; set; }
        public SyncStatus? Status { get; set; }
        public bool IsDryRun { get; set; }
        public int? ThresholdViolations { get; set; }
    }
}