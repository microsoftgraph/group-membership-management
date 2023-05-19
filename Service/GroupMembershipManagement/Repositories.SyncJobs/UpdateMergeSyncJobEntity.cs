// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json;
using System;

namespace Repositories.SyncJobsRepository.Entities
{
    internal class UpdateMergeSyncJobEntity : ITableEntity
    {
        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public DateTime StartDate { get; set; } = DateTime.FromFileTimeUtc(0);

        public DateTimeOffset? Timestamp { get; set; }

        [JsonIgnore]
        public ETag ETag { get; set; }

        public UpdateMergeSyncJobEntity() { }
    }
}
