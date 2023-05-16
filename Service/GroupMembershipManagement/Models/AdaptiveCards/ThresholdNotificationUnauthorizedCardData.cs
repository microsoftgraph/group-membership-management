// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.AdaptiveCards
{
    public class ThresholdNotificationUnauthorizedCardData
    {
        public string GroupName { get; set; }
        public string NotificationId { get; set; }
        public string ProviderId { get; set; }
        public DateTime CardCreatedTime { get; set; }
    }
}
