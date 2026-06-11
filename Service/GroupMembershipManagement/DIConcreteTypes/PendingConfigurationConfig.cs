// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class PendingConfigurationConfig : IPendingConfigurationConfig
    {
        public bool PendingConfigurationIsEnabled { get; set; }

        public PendingConfigurationConfig()
        {
        }

        public PendingConfigurationConfig(bool pendingConfigurationIsEnabled)
        {
            PendingConfigurationIsEnabled = pendingConfigurationIsEnabled;
        }
    }
}
