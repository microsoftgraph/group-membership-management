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
        public string BackUpTo { get; }
        public int DeleteAfterDays { get; }

        public AzureTableBackup(string sourceTableName, string sourceConnectionString, string destinationConnectionString, string backUpTo, int deleteAfterDays)
        {
            this.SourceTableName = sourceTableName;
            this.SourceConnectionString = sourceConnectionString;
            this.DestinationConnectionString = destinationConnectionString;
            this.BackUpTo = backUpTo;
            this.DeleteAfterDays = deleteAfterDays;
        }
    }
}
