// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models.ThresholdNotifications
{
    public enum ThresholdNotificationStatus
    {
        /// <summary>
        /// Unknown state.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// Notification has been added to the notifications table by GMM.
        /// </summary>
        Queued = 1,
        /// <summary>
        /// The notificaton trigger has dispatched the notificaton to the notifier function.
        /// </summary>
        Triggered = 2,
        /// <summary>
        /// The notifier function has received the notification.
        /// </summary>
        Processing = 3,
        /// <summary>
        /// The notifier has emailed the notification recipients.
        /// </summary>
        AwaitingResponse = 4,
        /// <summary>
        /// A notification recipient has responded to the notification.
        /// </summary>
        ResponseReceived = 5,
        /// <summary>
        /// The notification is resolved.
        /// </summary>
        Resolved = 6
    }
}
