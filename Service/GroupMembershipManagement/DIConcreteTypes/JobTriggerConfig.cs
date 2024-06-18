// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
public class JobTriggerConfig : IJobTriggerConfig
    {
        public JobTriggerConfig() {}

        public JobTriggerConfig(bool gmmHasGroupReadWriteAllPermissions, bool gmmHasChannelReadWriteAllPermissions, int jobCountThreshold, int jobPerMilleThreshold)
        {
            GMMHasGroupReadWriteAllPermissions = gmmHasGroupReadWriteAllPermissions;
            GMMHasChannelReadWriteAllPermissions = gmmHasChannelReadWriteAllPermissions;
            JobCountThreshold = jobCountThreshold;
            JobPerMilleThreshold = jobPerMilleThreshold;
        }

        public bool GMMHasGroupReadWriteAllPermissions { get; set; }
        public bool GMMHasChannelReadWriteAllPermissions { get; set; }
        public int JobCountThreshold { get; set; }  
        public int JobPerMilleThreshold { get; set; }  
    }
}
