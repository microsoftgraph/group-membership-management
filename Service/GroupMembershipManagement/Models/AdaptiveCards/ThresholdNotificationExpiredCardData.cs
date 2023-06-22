// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.AdaptiveCards
{
    public class ThresholdNotificationExpiredCardData
    {
        public string NotificationId { get; set; }
        public string ProviderId { get; set; }
        public string GroupId { get; set; }
        public DateTime CardCreatedTime { get; set; }
    }
}
