// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using Models.CustomAttributes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Models;
using System.Diagnostics.CodeAnalysis;
using Models.ThresholdNotifications;
using System.Runtime.Serialization;

namespace Repositories.NotificationsRepository
{
    [ExcludeFromCodeCoverage]
    internal class ThresholdNotificationEntity : ITableEntity
    {
        public ThresholdNotificationEntity()
        {
        }

        public ThresholdNotificationEntity(string partitionKey, string rowKey)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }
        public Guid SyncJobId { get; set; }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }

        /// <summary>
        /// The threshold notification id.
        /// </summary>
        public Guid Id { get; set; } = Guid.Empty;

        /// <summary>
        /// The threshold notification sync job's PartitionKey.
        /// </summary>
        public string SyncJobPartitionKey { get; set; } = string.Empty;

        /// <summary>
        /// The threshold notification  sync job's RowKey.
        /// </summary>
        public string SyncJobRowKey { get; set; } = string.Empty;

        /// <summary>
        /// The id of the group associated with the notification.
        /// </summary>
        public Guid TargetOfficeGroupId { get; set; }

        /// <summary>
        /// Gets or sets the notification status name to persist in the azure table store.
        /// </summary>
        public string StatusName { get; set; }

        ///// <summary>
        ///// The notification status.
        ///// </summary>
        [IgnoreDataMember]
        public ThresholdNotificationStatus? Status
        {
            get
            {
                if (string.IsNullOrEmpty(StatusName))
                {
                    return null;
                }

                return (ThresholdNotificationStatus)Enum.Parse(typeof(ThresholdNotificationStatus), StatusName);
            }

            set
            {
                StatusName = value?.ToString();
            }
        }


        /// <summary>
        /// The allowed change size of users to be added to the group as a percentage of the current group size.
        /// </summary>
        public int ThresholdPercentageForAdditions { get; set; } = 0;

        /// <summary>
        /// The allowed change size of users to be removed from the group as a percentage of the current group size.
        /// </summary>
        public int ThresholdPercentageForRemovals { get; set; } = 0;

        /// <summary>
        /// The percentage of users to be added as a percentage of the current group size.
        /// </summary>
        public int ChangePercentageForAdditions { get; set; } = 0;

        /// <summary>
        /// The percentage of users to be removed as a percentage of the current group size.
        /// </summary>
        public int ChangePercentageForRemovals { get; set; } = 0;

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
        public string ResolvedByUPN { get; set; } = string.Empty;

        /// <summary>
        /// The action taken to resolve the notification.
        /// </summary>
        public string ResolutionName { get; set; }

        [IgnoreDataMember]
        public ThresholdNotificationResolution? Resolution
        {
            get
            {
                if (string.IsNullOrEmpty(ResolutionName))
                {
                    return null;
                }

                return (ThresholdNotificationResolution)Enum.Parse(typeof(ThresholdNotificationResolution), ResolutionName);
            }

            set
            {
                ResolutionName = value?.ToString();
            }
        }

        /// <summary>
        /// The state of the notification card and what type of card should be sent out in the next email
        /// </summary>
        public string CardStateName { get; set; }

        [IgnoreDataMember]
        public ThresholdNotificationCardState? CardState
        {
            get
            {
                if (string.IsNullOrEmpty(CardStateName))
                {
                    return null;
                }

                return (ThresholdNotificationCardState)Enum.Parse(typeof(ThresholdNotificationCardState), CardStateName);
            }

            set
            {
                CardStateName = value?.ToString();
            }
        }

        [JsonIgnore]
        public DateTimeOffset? Timestamp { get; set; }

        [JsonIgnore]
        public ETag ETag { get; set; }

    }
}
