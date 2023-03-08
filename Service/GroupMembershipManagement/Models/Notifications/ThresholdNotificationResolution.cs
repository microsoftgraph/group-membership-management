// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models.ThresholdNotifications
{
    public enum ThresholdNotificationResolution
    {
        /// <summary>
        /// The notification is unresolved.
        /// </summary>
        Unresolved = 0,
        /// <summary>
        /// The notification was resolved by overriding the threshold for the next sync.
        /// </summary>
        ThresholdOverride = 1,
        /// <summary>
        /// The notification was resolved by adjusting a sync job threshold.
        /// </summary>
        ThresholdAdjusted = 2,
        /// <summary>
        /// The notification was resolved by pausing the sync job
        /// </summary>
        Paused = 3
    }
}
