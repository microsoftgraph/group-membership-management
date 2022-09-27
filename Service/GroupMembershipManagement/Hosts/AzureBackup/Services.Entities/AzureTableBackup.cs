// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace Services.Entities
{
    public class AzureBackup : IAzureBackup
    {
        public string SourceTableName { get; }
        public string SourceConnectionString { get; }
        public string DestinationConnectionString { get; }
        public string BackupType { get; }
        public bool CleanupOnly { get; }
        public int DeleteAfterDays { get; }

        public AzureBackup(string sourceTableName, string sourceConnectionString, string destinationConnectionString, string backupType, bool cleanupOnly, int deleteAfterDays)
        {
            SourceTableName = sourceTableName;
            SourceConnectionString = sourceConnectionString;
            DestinationConnectionString = destinationConnectionString;
            BackupType = backupType;
            CleanupOnly = cleanupOnly;
            DeleteAfterDays = deleteAfterDays;
        }

        public override bool Equals(object other)
        {
            if (other == null)
            {
                return false;
            }

            var otherBackup = other as AzureBackup;

            return SourceTableName == otherBackup.SourceTableName
                && SourceConnectionString == otherBackup.SourceConnectionString
                && DestinationConnectionString == otherBackup.DestinationConnectionString
                && BackupType == otherBackup.BackupType
                && CleanupOnly == otherBackup.CleanupOnly
                && DeleteAfterDays == otherBackup.DeleteAfterDays;
        }

        public override int GetHashCode()
        {
            return SourceTableName.GetHashCode()
                ^ SourceConnectionString.GetHashCode()
                ^ DestinationConnectionString.GetHashCode()
                ^ BackupType.GetHashCode()
                ^ CleanupOnly.GetHashCode()
                ^ DeleteAfterDays.GetHashCode();
        }
    }
}
