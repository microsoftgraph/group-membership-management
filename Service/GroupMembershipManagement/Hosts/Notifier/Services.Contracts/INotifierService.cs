// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ThresholdNotifications;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface INotifierService
    {
        public Task SendEmailAsync(Guid targetOfficeGroupId);
        public Task<List<ThresholdNotification>> RetrieveQueuedNotificationsAsync();
        public Task UpdateNotificationStatusAsync(ThresholdNotification notification, ThresholdNotificationStatus status);
    }
}
