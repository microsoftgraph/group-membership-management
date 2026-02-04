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
        
        /// <summary>
        /// When true, jobs with thresholds are prioritized over jobs without thresholds during distribution.
        /// </summary>
        public bool PrioritizeThresholdJobs { get; set; }
    }
}
