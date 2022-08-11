// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using Entities;
using Entities.CustomAttributes;
using Newtonsoft.Json;
using System;

namespace Entities
{
    public class UpdateMergeSyncJob : ITableEntity
    {
        public string PartitionKey { get; set; }

        public string RowKey { get; set; }
        
        public DateTime StartDate { get; set; } = DateTime.FromFileTimeUtc(0);

        public DateTimeOffset? Timestamp { get; set; }

        [JsonIgnore]
        public ETag ETag { get; set; }

        public UpdateMergeSyncJob(SyncJob syncJob) {
            PartitionKey = syncJob.PartitionKey;
            RowKey = syncJob.RowKey;
            StartDate = syncJob.StartDate;
        }

        public UpdateMergeSyncJob() { }
    }
}
