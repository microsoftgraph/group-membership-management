// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.CustomAttributes;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class SyncJob : TableEntity
    {
        public SyncJob()
        {
        }

        public SyncJob(string partitionKey, string rowKey)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }

        public Guid? RunId { get; set; }

        [IgnoreLogging]
        public string Requestor { get; set; }

        /// <summary>
        /// Synchronization type
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Office group AD id
        /// </summary>
        public Guid TargetOfficeGroupId { get; set; }

        [IgnoreLogging]
        public string Status { get; set; }

        /// <summary>
        /// Last Run Time (UTC)
        /// </summary>
        [IgnoreLogging]
        public DateTime LastRunTime { get; set; }

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
        public DateTime StartDate { get; set; }

        [IgnoreLogging]
        public bool Enabled { get; set; }

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

        public Dictionary<string, string> ToDictionary() =>
            DictionaryHelper.ToDictionary(this, new DictionaryHelper.Options
            {
                UseCamelCase = true,
                PropertiesToIgnore = new List<string> { "Timestamp", "ETag" }
            });
    }
}
