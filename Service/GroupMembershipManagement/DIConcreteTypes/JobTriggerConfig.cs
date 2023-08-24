// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
public class JobTriggerConfig : IJobTriggerConfig
    {
        public JobTriggerConfig() {}

        public JobTriggerConfig(bool gmmHasGroupReadWriteAllPermissions, int MinimumJobsToTriggerRun, int jobsPercentageToStopTriggeringRuns)
        {
            GMMHasGroupReadWriteAllPermissions = gmmHasGroupReadWriteAllPermissions;
            MinimumJobsToTriggerRun = MinimumJobsToTriggerRun;
            jobsPercentageToStopTriggeringRuns = jobsPercentageToStopTriggeringRuns;
        }

        public bool GMMHasGroupReadWriteAllPermissions { get; set; }
        public int MinimumJobsToTriggerRun { get; set; }  
        public int jobsPercentageToStopTriggeringRuns { get; set; }  
    }
}
