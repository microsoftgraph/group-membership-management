// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.Entities;
using System;
using System.Collections.Generic;

namespace Hosts.GroupOwnershipObtainer
{
    public partial class JobsFilterRequest
    {
        public SyncJob SyncJob { get; set; }
        public int CurrentPart { get; set; }
        public int TotalParts { get; set; }
        public List<JobsFilterSyncJob> SyncJobs { get; set; }
        public HashSet<string> RequestedSources { get; set; }
    }
}
