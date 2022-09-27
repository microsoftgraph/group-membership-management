// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IAzureBackup
    {
        string SourceTableName { get; }
        string SourceConnectionString { get; }
        string DestinationConnectionString { get; }
        string BackupType { get; }
        bool CleanupOnly { get; }
        int DeleteAfterDays { get; }
    }
}
