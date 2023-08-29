// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
public class JobTriggerConfig : IJobTriggerConfig
    {
        public JobTriggerConfig() {}

        public JobTriggerConfig(bool gmmHasGroupReadWriteAllPermissions, int jobCountThreshold, int jobPercentThreshold)
        {
            GMMHasGroupReadWriteAllPermissions = gmmHasGroupReadWriteAllPermissions;
            JobCountThreshold = jobCountThreshold;
            JobPercentThreshold = jobPercentThreshold;
        }

        public bool GMMHasGroupReadWriteAllPermissions { get; set; }
        public int JobCountThreshold { get; set; }  
        public int JobPercentThreshold { get; set; }  
    }
}
