// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Hosts.TeamsChannelUpdater
{
    public class JobStatusUpdaterRequest
    {
        public Guid RunId { get; set; }
        public Guid SyncJobId { get; set; }
        public SyncStatus Status { get; set; }
        public int ThresholdViolations { get; set; }
    }
}