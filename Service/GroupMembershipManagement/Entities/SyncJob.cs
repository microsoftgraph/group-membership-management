// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Cosmos.Table;
using System;
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
        public string Owner { get; set; }
        /// <summary>
        /// Syncronization type
        /// </summary>
        public string Type { get; set; }
        /// <summary>
        /// Office group AD id
        /// </summary>
        public Guid TargetOfficeGroupId { get; set; }
        public string Status { get; set; }
        /// <summary>
        /// Last Run Time (UTC)
        /// </summary>
        public DateTime LastRunTime { get; set; }
        /// <summary>
        /// Period (hours)
        /// </summary>
        public int Period { get; set; }
        public string Query { get; set; }
        /// <summary>
        /// Start Date (UTC)
        /// </summary>
        public DateTime StartDate { get; set; }
        public bool Enabled { get; set; }
    }
}

