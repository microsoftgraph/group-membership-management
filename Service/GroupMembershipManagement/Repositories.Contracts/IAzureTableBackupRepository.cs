// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.AzureTableBackup;
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts.InjectConfig;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAzureTableBackupRepository : IAzureStorageBackupRepository
    {
        Task<List<DynamicTableEntity>> GetEntitiesAsync(IAzureTableBackup backupSettings);
        Task AddBackupResultTrackerAsync(IAzureTableBackup backupSettings, BackupResult backupResult);
        Task DeleteBackupTrackersAsync(IAzureTableBackup backupSettings, List<(string PartitionKey, string RowKey)> keys);
        Task<BackupResult> GetLastestBackupResultTrackerAsync(IAzureTableBackup backupSettings);
    }
}
