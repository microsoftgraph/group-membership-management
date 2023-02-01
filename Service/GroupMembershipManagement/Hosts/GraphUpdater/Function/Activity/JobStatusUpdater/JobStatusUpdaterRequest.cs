// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.Entities;
using System;

namespace Hosts.GraphUpdater
{
    public class JobStatusUpdaterRequest
    {
        public Guid RunId { get; set; }
        public string JobPartitionKey { get; set; }
        public string JobRowKey { get; set; }
        public SyncStatus Status { get; set; }
        public int ThresholdViolations { get; set; }
    }
}