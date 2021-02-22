// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Entities.AzureTableBackup
{
    public class BackupResult
    {
        public string TableName { get; }
        public int RowCount { get; }

        public BackupResult(string tableName, int rowCount)
        {
            this.TableName = tableName;
            this.RowCount = rowCount;
        }
    }
}
