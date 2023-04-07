// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.ThresholdNotifications;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockNotificationRepository : INotificationRepository
    {
        public Dictionary<(string, string), ThresholdNotification> ExistingNotifications = new Dictionary<(string, string), ThresholdNotification>();

        public async Task<ThresholdNotification> GetThresholdNotificationByIdAsync(Guid notificationId)
        {
            var notification = ExistingNotifications.ContainsKey(("ThresholdNotification", notificationId.ToString())) ? ExistingNotifications[("ThresholdNotification", notificationId.ToString())] : null;
            return await Task.FromResult(notification);
        }
        public async Task SaveNotificationAsync(ThresholdNotification notification)
        {
            // Resolve warning CS1998 about async method signature without await keyword
            var _ = await Task.FromResult(Task.CompletedTask);
            return;
        }
    }
}
