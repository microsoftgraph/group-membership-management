// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ThresholdNotifications;

namespace Services.Contracts.Notifications
{
    public interface IThresholdNotificationService
    {
        /// <summary>
        /// Generates the adaptive card for a threshold notification email.
        /// </summary>
        /// <param name="notification">The threshold notification.</param>
        /// <returns>The adaptive card used to refresh notifications.</returns>
        Task<string> CreateNotificationCardAsync(ThresholdNotification notification);

        /// <summary>
        /// Generates the adaptive card for a threshold notification that has been resolved.
        /// </summary>
        /// <param name="notificationId">The threshold notification.</param>
        /// <returns>The adpative card used to indicate that the notification has been resolved.</returns>
        Task<string> CreateResolvedNotificationCardAsync(ThresholdNotification notification);

        /// <summary>
        /// Generates the adaptive card for a threshold notification that no longer exists.
        /// </summary>
        /// <param name="notificationId">The id of the notification.</param>
        /// <returns>The adpative card used to indicate that the notification no longer exists.</returns>
        Task<string> CreateNotFoundNotificationCardAsync(Guid notificationId);

        /// <summary>
        /// Generates the adaptive card for a threshold notification that a user is unauthorized to view or resolve.
        /// </summary>
        /// <param name="notification">The threshold notification.</param>
        /// <returns>The adpative card used to indicate that the user is unauthorized to view or resolve the notification.</returns>
        Task<string> CreateUnauthorizedNotificationCardAsync(ThresholdNotification notification);
    }
}