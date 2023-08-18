// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace Repositories.Mocks
{
    public class MockJobTriggerConfig : IJobTriggerConfig
    {
        public bool GMMHasGroupReadWriteAllPermissions { get; set; }
		public int MinimalJobs { get; set; }
		public int StopThreshold { get; set; }

		public MockJobTriggerConfig()
        {
            GMMHasGroupReadWriteAllPermissions = false;
            MinimalJobs = 4;
            StopThreshold = 20;

		}
    }
}
