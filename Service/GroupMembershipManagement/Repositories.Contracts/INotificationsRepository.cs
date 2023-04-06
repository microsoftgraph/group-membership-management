// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
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
        IAsyncEnumerable<ThresholdNotification> GetQueuedNotificationsAsync();
        Task UpdateNotificationStatusAsync(ThresholdNotification notification, ThresholdNotificationStatus status);
    }
}
