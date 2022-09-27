// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.AzureBackup;
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts.InjectConfig;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAzureBackupRepository : IAzureStorageBackupRepository
    {
        Task<List<DynamicTableEntity>> GetEntitiesAsync(IAzureBackup backupSettings);
        Task AddBackupResultTrackerAsync(IAzureBackup backupSettings, BackupResult backupResult);
        Task DeleteBackupTrackersAsync(IAzureBackup backupSettings, List<(string PartitionKey, string RowKey)> keys);
        Task<BackupResult> GetLastestBackupResultTrackerAsync(IAzureBackup backupSettings);
    }
}
