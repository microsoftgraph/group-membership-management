// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.CustomAttributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.SqlTypes;

namespace Models
{

    public class PurgedSyncJob
    {
        public PurgedSyncJob()
        {
        }

        public PurgedSyncJob(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; set; }

        public Guid? RunId { get; set; }

        [IgnoreLogging]
        public string Requestor { get; set; }

        /// <summary>
        /// Office group AD id
        /// </summary>
        public Guid TargetOfficeGroupId { get; set; }
        public string Destination { get; set; }
        public bool AllowEmptyDestination { get; set; }

        [IgnoreLogging]
        public string Status { get; set; }

        /// <summary>
        /// Last Run Time (UTC)
        /// </summary>
        [IgnoreLogging]
        public DateTime LastRunTime { get; set; } = SqlDateTime.MinValue.Value; //azure table storage rejects default(DateTime), so set them to this on construction.

        /// <summary>
        /// Last Successful Run Time (UTC)
        /// </summary>
        [IgnoreLogging]
        public DateTime LastSuccessfulRunTime { get; set; } = SqlDateTime.MinValue.Value;

        /// <summary>
        /// Last Successful Start Time (UTC)
        /// </summary>
        [IgnoreLogging]
        public DateTime LastSuccessfulStartTime { get; set; } = SqlDateTime.MinValue.Value;

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
        public DateTime StartDate { get; set; } = SqlDateTime.MinValue.Value;

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
                    _dryRunTimeStamp = SqlDateTime.MinValue.Value;
                else
                    _dryRunTimeStamp = value;
            }
        }

        /// <summary>
        /// Tracks how many threshold violations have occurred
        /// </summary>
        [IgnoreLogging]
        public int ThresholdViolations { get; set; }
        public DateTime PurgedAt { get; set; } = SqlDateTime.MinValue.Value;
        [NotMapped]
        public DateTimeOffset? Timestamp { get; set; }
        [NotMapped]
        public string ETag { get; set; }

        public Dictionary<string, string> ToDictionary() =>
            DictionaryHelper.ToDictionary(this, new DictionaryHelper.Options
            {
                UseCamelCase = false,
                PropertiesToIgnore = new List<string> { "Timestamp", "ETag" }
            });
    }
}
