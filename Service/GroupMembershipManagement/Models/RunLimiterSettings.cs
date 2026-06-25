// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    public class RunLimiterSettings
    {
        private const int DefaultMaxPendingAgeMinutes = 60;

        private int _maxPendingAgeMinutes;

        public bool IsEnabled { get; set; }
        public int MaxInFlightMessages { get; set; }
        public int LeaseTimeoutMinutes { get; set; }
        public int HeartbeatIntervalMinutes { get; set; }

        // A missing or non-positive value falls back to the default window.
        public int MaxPendingAgeMinutes
        {
            get => _maxPendingAgeMinutes > 0 ? _maxPendingAgeMinutes : DefaultMaxPendingAgeMinutes;
            set => _maxPendingAgeMinutes = value;
        }
    }
}
