// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Entities;
using System;
using System.Collections.Generic;

namespace Hosts.GroupOwnershipObtainer
{
    public partial class JobsFilterRequest
    {
        public Guid? RunId { get; set; }
        public List<JobsFilterSyncJob> SyncJobs { get; set; }
        public HashSet<string> RequestedSources { get; set; }
    }
}
