// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    public class RunLimiterSettings
    {
        public bool IsEnabled { get; set; }
        public int MaxInFlightMessages { get; set; }
        public int LeaseTimeoutMinutes { get; set; }
        public int HeartbeatIntervalMinutes { get; set; }
    }
}
