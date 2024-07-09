// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Models.ThresholdNotifications;

namespace Entities
{
    public class ThresholdNotification
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]

        /// <summary>
        /// The threshold notification id.
        /// </summary>
        public Guid Id { get; set; } = Guid.Empty;

        /// <summary>
        /// The id of the group associated with the notification.
        /// </summary>
        public Guid TargetOfficeGroupId { get; set; }

        /// <summary>
        /// The threshold notification sync job's Id.
        /// </summary>
        public Guid SyncJobId { get; set; }

        /// <summary>
        /// Gets or sets the notification status name to persist in the azure table store.
        /// </summary>
        public string StatusName { get; set; }

        [NotMapped]
        public ThresholdNotificationStatus? Status
        {
            get
            {
                if (string.IsNullOrEmpty(this.StatusName))
                {
                    return null;
                }

                return (ThresholdNotificationStatus)Enum.Parse(typeof(ThresholdNotificationStatus), this.StatusName);
            }

            set
            {
                this.StatusName = value?.ToString();
            }
        }


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
        public double ChangePercentageForAdditions { get; set; } = 0;

        /// <summary>
        /// The percentage of users to be removed as a percentage of the current group size.
        /// </summary>
        public double ChangePercentageForRemovals { get; set; } = 0;
        
        /// <summary>
        /// The number of users to be added to the current group;
        /// </summary>
        public int ChangeQuantityForAdditions { get; set; } = 0;

        /// <summary>
        /// The number of users to be removed from the current group.
        /// </summary>
        public int ChangeQuantityForRemovals { get; set; } = 0;

        /// <summary>
        /// The time the notification was created.
        /// </summary>
        public DateTime CreatedTime { get; set; } = DateTime.FromFileTimeUtc(0);

        /// <summary>
        /// The time the notification was resolved.
        /// </summary>
        public DateTime ResolvedTime { get; set; } = DateTime.FromFileTimeUtc(0);

        /// <summary>
        /// The UPN of the person who resolved the notification.
        /// </summary>
        public string ResolvedBy { get; set; } = string.Empty;
        
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public DateTime LastUpdatedTime { get; set; }

        /// <summary>
        /// The action taken to resolve the notification.
        /// </summary>
        public string ResolutionName { get; set; }

        [NotMapped]
        public ThresholdNotificationResolution? Resolution
        {
            get
            {
                if (string.IsNullOrEmpty(this.ResolutionName))
                {
                    return null;
                }

                return (ThresholdNotificationResolution)Enum.Parse(typeof(ThresholdNotificationResolution), this.ResolutionName);
            }
            set
            {
                this.ResolutionName = value.HasValue ? value.ToString() : null;
            }
        }
        public string CardStateName { get; set; }

        [NotMapped]
        public ThresholdNotificationCardState? CardState
        {
            get
            {
                if (string.IsNullOrEmpty(this.CardStateName))
                {
                    return null;
                }

                return (ThresholdNotificationCardState)Enum.Parse(typeof(ThresholdNotificationCardState), this.CardStateName);
            }
            set
            {
                this.ResolutionName = value.HasValue ? value.ToString() : null;
            }
        }
    }
}
