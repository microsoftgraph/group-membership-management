// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.ThresholdNotifications;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Notifications.Tests
{
    public class MockNotificationRepository : INotificationRepository
    {
        public List<ThresholdNotification> ThresholdNotifications { get; set; } = new List<ThresholdNotification>();

        public async Task<ThresholdNotification> GetThresholdNotificationByIdAsync(Guid notificationId)
        {
            var thresholdNotification = ThresholdNotifications.FirstOrDefault(x => x.Id == notificationId);
            return await Task.FromResult(thresholdNotification);
        }

        public async Task SaveNotificationAsync(ThresholdNotification notification)
        {
            await Task.CompletedTask;
        }

        public async Task<ThresholdNotification> GetThresholdNotificationBySyncJobKeysAsync(Guid syncJobId)
        {
            var thresholdNotification = ThresholdNotifications.FirstOrDefault(x => x.SyncJobId == syncJobId);
            return await Task.FromResult(thresholdNotification);
        }

        public IAsyncEnumerable<ThresholdNotification> GetQueuedNotificationsAsync()
        {
            throw new NotImplementedException();
        }
        public Task UpdateNotificationStatusAsync(ThresholdNotification notification, ThresholdNotificationStatus status)
        {
            throw new NotImplementedException();
        }
    }
}