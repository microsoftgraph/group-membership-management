// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models.ThresholdNotifications
{
    public enum ThresholdNotificationCardState
    {
        /// <summary>
        /// Will no longer send out a new card
        /// </summary>
        NoCard = 0,
        /// <summary>
        /// Will send out the default card for threshold exceeded
        /// </summary>
        DefaultCard = 1,
        /// <summary>
        /// Will send out a Sync Disabled card
        /// </summary>
        DisabledCard = 2,
        /// <summary>
        /// Will send out an Expired card
        /// </summary>
        ExpiredCard = 3
    }
}
