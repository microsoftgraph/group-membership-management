// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class DistributeJobsRequest
    {
        public List<DistributionSyncJob> JobsToDistribute { get; set; }
        public int StartTimeDelayMinutes { get; set; }
        public int DelayBetweenSyncsSeconds { get; set; }
    }
}
