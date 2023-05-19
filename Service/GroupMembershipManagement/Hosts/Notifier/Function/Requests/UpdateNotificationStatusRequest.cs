// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ThresholdNotifications;

namespace Hosts.Notifier
{
    public class UpdateNotificationStatusRequest
    {
        public ThresholdNotification Notification { get; set; }
        public ThresholdNotificationStatus Status { get; set; }
    }
}