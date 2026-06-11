// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class ResetJobsRequest
    {
        public List<DistributionSyncJob> JobsToReset { get; set; }
        public int DaysToAddForReset { get; set; }
    }
}
