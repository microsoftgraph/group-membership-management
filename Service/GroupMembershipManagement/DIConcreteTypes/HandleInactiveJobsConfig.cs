// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class HandleInactiveJobsConfig : IHandleInactiveJobsConfig
    {
        public bool HandleInactiveJobsEnabled { get; set; }
        public HandleInactiveJobsConfig() {}

        public HandleInactiveJobsConfig(bool handleInactiveJobsEnabled)
        {
            HandleInactiveJobsEnabled = handleInactiveJobsEnabled;
        }
    }
}
