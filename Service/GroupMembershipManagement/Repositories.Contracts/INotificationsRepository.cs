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

        /// <summary>Gets the most recent notification for a sync job (any state) so the UI can show its resolved state.</summary>
        Task<ThresholdNotification> GetLatestThresholdNotificationBySyncJobIdAsync(Guid syncJobId);

        IAsyncEnumerable<ThresholdNotification> GetQueuedNotificationsAsync();
        Task UpdateNotificationStatusAsync(ThresholdNotification notification, ThresholdNotificationStatus status);
    }
}
