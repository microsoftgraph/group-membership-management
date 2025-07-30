// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class PendingConfigurationConfig : IPendingConfigurationConfig
    {
        public bool EnablePendingConfigurationStatus { get; set; }

        public PendingConfigurationConfig()
        {
        }

        public PendingConfigurationConfig(bool enablePendingConfigurationStatus)
        {
            EnablePendingConfigurationStatus = enablePendingConfigurationStatus;
        }
    }
}
