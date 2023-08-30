// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace Repositories.Mocks
{
    public class MockJobTriggerConfig : IJobTriggerConfig
    {
        public bool GMMHasGroupReadWriteAllPermissions { get; set; }
		public int JobCountThreshold { get; set; }
		public int JobPercentThreshold { get; set; }

		public MockJobTriggerConfig()
        {
            GMMHasGroupReadWriteAllPermissions = false;
            JobCountThreshold = 4;
            JobPercentThreshold = 25;

		}
    }
}
