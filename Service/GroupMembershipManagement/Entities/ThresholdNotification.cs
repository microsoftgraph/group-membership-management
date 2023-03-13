// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json;
using System;
using System.Diagnostics.CodeAnalysis;
using Models.ThresholdNotifications;
using System.Runtime.Serialization;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class ThresholdNotification : ITableEntity
    {
        public ThresholdNotification()
        {
        }

        public ThresholdNotification(string partitionKey, string rowKey)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }

        /// <summary>
        /// The threshold notification id.
        /// </summary>
        public Guid Id { get; set; } = Guid.Empty;

        /// <summary>
        /// The id of the group associated with the notification.
        /// </summary>
        public Guid TargetOfficeGroupId { get; set; }

        /// <summary>
        /// Gets or sets the notification status name to persist in the azure table store.
        /// </summary>
        public string StatusName { get; set; }

        [IgnoreDataMember]
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
        public int ChangePercentageForAdditions { get; set; } = 0;

        /// <summary>
        /// The percentage of users to be removed as a percentage of the current group size.
        /// </summary>
        public int ChangePercentageForRemovals { get; set; } = 0;

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
                if (string.IsNullOrEmpty(this.ResolutionName))
                {
                    return null;
                }

                return (ThresholdNotificationResolution)Enum.Parse(typeof(ThresholdNotificationResolution), this.ResolutionName);
            }
        }

        [JsonIgnore]
        public DateTimeOffset? Timestamp { get; set; }

        [JsonIgnore]
        public ETag ETag { get; set; }
    }
}
