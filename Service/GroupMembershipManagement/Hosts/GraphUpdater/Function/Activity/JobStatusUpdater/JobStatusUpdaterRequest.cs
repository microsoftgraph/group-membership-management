// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Entities;

namespace Hosts.GraphUpdater
{
    public class JobStatusUpdaterRequest : GraphUpdaterRequestBase
    {
        public SyncStatus Status { get; set; }
        public int ThresholdViolations { get; set; }
        public int UsersAdded { get; set; } = 0;
        public int UsersRemoved { get; set; } = 0;
    }
}