// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace Services.Entities
{
    public class AzureTableBackup : IAzureTableBackup
    {
        public string SourceTableName { get; }
        public string SourceConnectionString { get; }
        public string DestinationConnectionString { get; }
        public string BackupType { get; }
        public bool CleanupOnly { get; }
        public int DeleteAfterDays { get; }

        public AzureTableBackup(string sourceTableName, string sourceConnectionString, string destinationConnectionString, string backupType, bool cleanupOnly, int deleteAfterDays)
        {
            SourceTableName = sourceTableName;
            SourceConnectionString = sourceConnectionString;
            DestinationConnectionString = destinationConnectionString;
            BackupType = backupType;
            CleanupOnly = cleanupOnly;
            DeleteAfterDays = deleteAfterDays;
        }
    }
}
