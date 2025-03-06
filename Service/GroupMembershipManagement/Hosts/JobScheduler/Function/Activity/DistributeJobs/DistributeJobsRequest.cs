// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Hosts.JobScheduler
{
    public class DistributeJobsRequest
    {
        public List<DistributionSyncJob> JobsToDistribute;
        public int StartTimeDelayMinutes;
        public int DelayBetweenSyncsSeconds;
    }
}
