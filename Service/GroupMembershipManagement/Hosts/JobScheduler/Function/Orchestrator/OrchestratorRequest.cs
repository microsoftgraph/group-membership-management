// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.JobScheduler
{
    public class OrchestratorRequest
    {
        public int StartTimeDelayMinutes { get; set; }
        
        /// <summary>
        /// When true, jobs with thresholds are prioritized over jobs without thresholds during distribution.
        /// This is typically set to true during pipeline runs to ensure threshold-enabled jobs run first.
        /// </summary>
        public bool PrioritizeThresholdJobs { get; set; }
    }
}
