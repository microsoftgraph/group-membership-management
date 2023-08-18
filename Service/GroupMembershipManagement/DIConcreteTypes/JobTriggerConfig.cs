// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
public class JobTriggerConfig : IJobTriggerConfig
    {
        public JobTriggerConfig() {}

        public JobTriggerConfig(bool gmmHasGroupReadWriteAllPermissions, int minimalJobs, int stopThreshold)
        {
            GMMHasGroupReadWriteAllPermissions = gmmHasGroupReadWriteAllPermissions;
            MinimalJobs = minimalJobs;
            StopThreshold = stopThreshold;
        }

        public bool GMMHasGroupReadWriteAllPermissions { get; set; }
        public int MinimalJobs { get; set; }  
        public int StopThreshold { get; set; }  
    }
}
