// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace Hosts.MembershipAggregator
{
    public class JobStatusUpdaterRequest
    {
        public SyncJob SyncJob { get; set; }
        public SyncStatus Status { get; set; }
    }
}