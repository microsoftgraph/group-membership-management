// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ThresholdNotifications;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface INotificationRepository
    {
        Task<ThresholdNotification> GetThresholdNotificationByIdAsync(Guid notificationId);
        Task SaveNotificationAsync(ThresholdNotification notification);
        Task<ThresholdNotification> GetThresholdNotificationBySyncJobIdAsync(Guid syncJobId);
        IAsyncEnumerable<ThresholdNotification> GetQueuedNotificationsAsync();
        Task UpdateNotificationStatusAsync(ThresholdNotification notification, ThresholdNotificationStatus status);
    }
}
