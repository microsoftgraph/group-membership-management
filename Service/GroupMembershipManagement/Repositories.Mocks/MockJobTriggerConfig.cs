// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace Repositories.Mocks
{
    public class MockJobTriggerConfig : IJobTriggerConfig
    {
        public bool GMMHasGroupReadWriteAllPermissions { get; set; }
        public bool GMMHasChannelReadWriteAllPermissions { get; set; }
        public int JobCountThreshold { get; set; }
		public int JobPerMilleThreshold { get; set; }

		public MockJobTriggerConfig()
        {
            GMMHasGroupReadWriteAllPermissions = false;
            GMMHasChannelReadWriteAllPermissions = false;
            JobCountThreshold = 5;
            JobPerMilleThreshold = 250;

		}
    }
}
