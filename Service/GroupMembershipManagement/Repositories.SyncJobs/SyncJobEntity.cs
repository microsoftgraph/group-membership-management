// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using Models.CustomAttributes;
using Newtonsoft.Json;
using System;

namespace Repositories.SyncJobsRepository.Entities
{
    internal class SyncJobEntity : ITableEntity
    {
        public SyncJobEntity()
        {
        }

        public SyncJobEntity(string partitionKey, string rowKey)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }

        public Guid? RunId { get; set; }

        [IgnoreLogging]
        public string Requestor { get; set; }

        /// <summary>
        /// Office group AD id
        /// </summary>
        public Guid TargetOfficeGroupId { get; set; }
        public string Destination { get; set; }

        public string DestinationType { get; set; }

        [IgnoreLogging]
        public string Status { get; set; }

        /// <summary>
        /// Last Run Time (UTC)
        /// </summary>
        [IgnoreLogging]
        public DateTime LastRunTime { get; set; } = DateTime.FromFileTimeUtc(0); //azure table storage rejects default(DateTime), so set them to this on construction.

        /// <summary>
        /// Last Successful Run Time (UTC)
        /// </summary>
        [IgnoreLogging]
        public DateTime LastSuccessfulRunTime { get; set; } = DateTime.FromFileTimeUtc(0);

        /// <summary>
        /// Last Successful Start Time (UTC)
        /// </summary>
        [IgnoreLogging]
        public DateTime LastSuccessfulStartTime { get; set; } = DateTime.FromFileTimeUtc(0);

        /// <summary>
        /// Period (hours)
        /// </summary>
        [IgnoreLogging]
        public int Period { get; set; }

        public string Query { get; set; }

        /// <summary>
        /// Start Date (UTC)
        /// </summary>
        [IgnoreLogging]
        public DateTime StartDate { get; set; } = DateTime.FromFileTimeUtc(0);

        /// <summary>
        /// Ignore threshold check if this is set to true
        /// </summary>
        [IgnoreLogging]
        public bool IgnoreThresholdOnce { get; set; }

        /// <summary>
        /// Threshold percentage for users being added
        /// </summary>
        [IgnoreLogging]
        public int ThresholdPercentageForAdditions { get; set; }

        /// <summary>
        /// Threshold percentage for users being removed
        /// </summary>
        [IgnoreLogging]
        public int ThresholdPercentageForRemovals { get; set; }

        /// <summary>
        /// Threshold percentage for users being removed
        /// </summary>
        [IgnoreLogging]
        public bool IsDryRunEnabled { get; set; }

        private DateTime _dryRunTimeStamp;
        /// <summary>
        /// Threshold percentage for users being removed
        /// </summary>
        [IgnoreLogging]
        public DateTime DryRunTimeStamp
        {
            get
            {
                return _dryRunTimeStamp;
            }
            set
            {
                if (DateTime.FromFileTimeUtc(0) > value)
                    _dryRunTimeStamp = DateTime.FromFileTimeUtc(0);
                else
                    _dryRunTimeStamp = value;
            }
        }

        /// <summary>
        /// Tracks how many threshold violations have occurred
        /// </summary>
        [IgnoreLogging]
        public int ThresholdViolations { get; set; }
        public DateTimeOffset? Timestamp { get; set; }

        [JsonIgnore]
        public ETag ETag { get; set; }
    }
}
