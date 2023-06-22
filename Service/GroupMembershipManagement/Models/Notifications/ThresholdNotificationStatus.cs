// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models.ThresholdNotifications
{
    public enum ThresholdNotificationStatus
    {
        /// <summary>
        /// All states.
        /// </summary>
        All = 0,
        /// <summary>
        /// Unknown state.
        /// </summary>
        Unknown = 1,
        /// <summary>
        /// Notification has been added to the notifications table by GMM.
        /// </summary>
        Queued = 2,
        /// <summary>
        /// The notificaton trigger has dispatched the notificaton to the notifier function.
        /// </summary>
        Triggered = 3,
        /// <summary>
        /// The notifier function has received the notification.
        /// </summary>
        Processing = 4,
        /// <summary>
        /// The notifier has emailed the notification recipients.
        /// </summary>
        AwaitingResponse = 5,
        /// <summary>
        /// The notification is resolved.
        /// </summary>
        Resolved = 6,
        /// <summary>
        /// The notification has expired.
        /// </summary>
        Expired = 7
    }
}
