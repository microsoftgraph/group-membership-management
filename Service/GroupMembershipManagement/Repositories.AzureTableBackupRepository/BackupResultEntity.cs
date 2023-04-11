// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Data.Tables;
using System;

namespace Repositories.SyncJobsRepository.Entities
{
    internal class BackupResultEntity : ITableEntity
    {
        public string BackupTableName { get; set; }
        public string BackedUpTo { get; set; }
        public int RowCount { get; set; }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public BackupResultEntity()
        {
        }

        public BackupResultEntity(string backupTableName, string backedUpTo, int rowCount)
        {
            this.BackupTableName = backupTableName;
            this.BackedUpTo = backedUpTo;
            this.RowCount = rowCount;
        }
    }
}
