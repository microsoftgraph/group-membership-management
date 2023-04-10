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
        public Task<List<ThresholdNotification>> RetrieveQueuedNotifications();
        public Task UpdateNotificationStatus(ThresholdNotification notification, ThresholdNotificationStatus status);
    }
}
