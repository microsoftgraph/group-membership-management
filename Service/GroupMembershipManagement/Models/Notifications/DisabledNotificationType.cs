// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace Models.Notifications
{
    public class DisabledNotificationType
    {
        public static readonly Dictionary<NotificationMessageType, string> StatusDescriptions = new Dictionary<NotificationMessageType, string>
        {
            { NotificationMessageType.DestinationNotExistNotification, "Destination Does Not Exist" },
            { NotificationMessageType.NotOwnerNotification, "Not Owner Of Destination" },
            { NotificationMessageType.NotValidSourceNotification, "Source Not Valid" },
            { NotificationMessageType.SourceNotExistNotification, "Source Does Not Exist" },
            { NotificationMessageType.GuestUserFailureNotification, "Guest Users Cannot Be Added To Unified Group" }
        };
    }
}