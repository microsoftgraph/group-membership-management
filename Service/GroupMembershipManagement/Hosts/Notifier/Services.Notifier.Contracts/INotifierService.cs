// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ThresholdNotifications;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Notifier.Contracts
{
    public interface INotifierService
    {
        public Task SendEmailAsync(ThresholdNotification notification);
        public Task<List<ThresholdNotification>> RetrieveQueuedNotificationsAsync();
        public Task UpdateNotificationStatusAsync(ThresholdNotification notification, ThresholdNotificationStatus status);
        public Task<Models.ThresholdNotifications.ThresholdNotification> CreateActionableNotificationFromContentAsync(Dictionary<string, object>  messageContent);

    }
}
