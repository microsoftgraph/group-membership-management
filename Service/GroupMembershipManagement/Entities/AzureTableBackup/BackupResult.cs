// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Cosmos.Table;

namespace Entities.AzureTableBackup
{
    public class BackupResult : TableEntity
    {
        public string BackupTableName { get; set; }
        public int RowCount { get; set; }

        public BackupResult()
        {
        }

        public BackupResult(string backupTableName, int rowCount)
        {
            this.BackupTableName = backupTableName;
            this.RowCount = rowCount;
        }
    }
}
