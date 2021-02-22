// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IAzureTableBackup
    {
        string SourceTableName { get; }
        string SourceConnectionString { get; }
        string DestinationConnectionString { get; }
        int DeleteAfterDays { get; }
    }
}
