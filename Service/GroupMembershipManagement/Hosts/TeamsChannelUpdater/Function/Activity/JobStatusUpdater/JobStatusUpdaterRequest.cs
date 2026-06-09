// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.TeamsChannelUpdater
{
    public class JobStatusUpdaterRequest
    {
        public Guid JobId { get; set; }
        public SyncStatus Status { get; set; }
        public int ThresholdViolations { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}