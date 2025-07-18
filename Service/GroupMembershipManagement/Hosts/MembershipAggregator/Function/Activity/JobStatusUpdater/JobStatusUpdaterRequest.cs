// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Entities;

namespace Hosts.MembershipAggregator
{
    public class JobStatusUpdaterRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required SyncStatus Status { get; init; }
        public required bool IsDryRun { get; init; }
        public required bool IncrementThresholdViolations { get; init; }
        public required MembershipDeltaStatus DeltaStatus { get; init; }
    }
}