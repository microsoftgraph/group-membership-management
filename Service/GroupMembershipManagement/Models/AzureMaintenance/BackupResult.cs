// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models.AzureMaintenance
{
    public class BackupResult
    {
        public string BackupTableName { get; set; }
        public string BackedUpTo { get; set; }
        public int RowCount { get; set; }

        public BackupResult()
        {
        }

        public BackupResult(string backupTableName, string backedUpTo, int rowCount)
        {
            this.BackupTableName = backupTableName;
            this.BackedUpTo = backedUpTo;
            this.RowCount = rowCount;
        }
    }
}
