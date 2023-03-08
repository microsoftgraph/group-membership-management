// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ThresholdNotifications;

namespace Services.Contracts.Notifications
{
    public interface IThresholdNotificationService
    {
        /// <summary>
        /// Generates the html for a threshold notification email.
        /// </summary>
        /// <param name="notification">The threshold notification.</param>
        /// <returns></returns>
        Task<string> CreateNotificationHTMLAsync(ThresholdNotification notification);

        /// <summary>
        /// Retrieves threshold notification details by id.
        /// </summary>
        /// <param name="id">The threshold notification id.</param>
        Task<ThresholdNotification?> GetNotificationAsync(Guid id);

        /// <summary>
        /// Retrieves the email addresses of owners of the group associated with the notification.
        /// </summary>
        /// <param name="notification">The threshold notification.</param>
        /// <returns></returns>
        Task<List<string>> GetRecipientEmailAddressesAsync(ThresholdNotification notification);

        /// <summary>
        /// Saves the threshold notification.
        /// </summary>
        /// <param name="notification">Saves the threshold notification.</param>
        /// <returns></returns>
        Task SaveNotificationAsync(ThresholdNotification notification);
    }
}