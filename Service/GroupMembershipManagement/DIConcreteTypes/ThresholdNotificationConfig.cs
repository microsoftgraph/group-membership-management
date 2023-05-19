// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class ThresholdNotificationConfig : IThresholdNotificationConfig
    {
        public bool IsThresholdNotificationEnabled { get; set; }

        public ThresholdNotificationConfig(bool isThresholdNotificationEnabled)
        {
            IsThresholdNotificationEnabled = isThresholdNotificationEnabled;
        }

        public ThresholdNotificationConfig()
        {
        }
    }
}
