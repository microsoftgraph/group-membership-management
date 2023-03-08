// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Contracts.Notifications;
using Models.ThresholdNotifications;

namespace Services.Notifications
{
    public class ThresholdNotificationService : IThresholdNotificationService
    {
        /// <inheritdoc />
        public async Task<string> CreateNotificationHTMLAsync(ThresholdNotification notification)
        {
            string notificationHTML;

            if (notification.Status == ThresholdNotificationStatus.Resolved)
            {
                notificationHTML = "Threshold Notification Resolved template";
            }
            else
            {
                notificationHTML = "Threshold Notification template";
            }

            return await Task.FromResult(notificationHTML);
        }

        /// <inheritdoc />
        public async Task<ThresholdNotification?> GetNotificationAsync(Guid id)
        {
            var testGuid = Guid.Parse("12340000-0000-0000-0000-00000000abcd");
            var testNotification = new ThresholdNotification()
            {
                Id = testGuid
            };

            if (id == testGuid)
            {
                return await Task.FromResult(testNotification);
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<List<string>> GetRecipientEmailAddressesAsync(ThresholdNotification notification)
        {
            var testEmails = new List<string>
            {
                "user1@contoso.net",
                "user2@contoso.net",
                "user3@contoso.net"
            };

            return testEmails;
        }

        /// <inheritdoc />
        public async Task SaveNotificationAsync(ThresholdNotification notification)
        {
            throw new NotImplementedException();
        }
    }
}