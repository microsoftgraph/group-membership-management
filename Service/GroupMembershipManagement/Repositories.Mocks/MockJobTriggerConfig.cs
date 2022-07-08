// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace Repositories.Mocks
{
    public class MockJobTriggerConfig : IJobTriggerConfig
    {
        public bool GMMHasGroupReadWriteAllPermissions { get; set; }

        public MockJobTriggerConfig()
        {
            GMMHasGroupReadWriteAllPermissions = false;
        }
    }
}
