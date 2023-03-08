// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.ThresholdNotifications
{
    public class ThresholdNotification
    {
        /// <summary>
        /// The threshold notification id.
        /// </summary>
        public Guid Id { get; set; } = Guid.Empty;

        /// <summary>
        /// The id of the group associated with the notification.
        /// </summary>
        public Guid TargetOfficeGroupId { get; set; } = Guid.Empty;

        /// <summary>
        /// The notification status.
        /// </summary>
        public ThresholdNotificationStatus Status { get; set; } = ThresholdNotificationStatus.Unknown;

        /// <summary>
        /// The allowed change size of users to be added to the group as a percentage of the current group size.
        /// </summary>
        public int ThresholdPercentageForAdditions { get; set; } = 100;

        /// <summary>
        /// The allowed change size of users to be removed from the group as a percentage of the current group size.
        /// </summary>
        public int ThresholdPercentageForRemovals { get; set; } = 20;

        /// <summary>
        /// The percentage of users to be added as a percentage of the current group size.
        /// </summary>
        public int ChangePercentageForAdditions { get; set; } = 0;

        /// <summary>
        /// The percentage of users to be removed as a percentage of the current group size.
        /// </summary>
        public int ChangePercentageForRemovals { get; set; } = 0;

        /// <summary>
        /// The time the notification was created.
        /// </summary>
        public DateTimeOffset CreatedTime { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// The time the notification was resolved.
        /// </summary>
        public DateTimeOffset ResolvedTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The UPN of the person who resolved the notification.
        /// </summary>
        public string ResolvedByUPN { get; set; } = string.Empty;

        /// <summary>
        /// The action taken to resolve the notification.
        /// </summary>
        public ThresholdNotificationResolution Resolution { get; set; } = ThresholdNotificationResolution.Unresolved;

    }
}
