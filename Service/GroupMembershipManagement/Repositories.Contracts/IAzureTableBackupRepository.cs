// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.AzureTableBackup;
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts.InjectConfig;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAzureTableBackupRepository
    {
        Task<List<BackupTable>> GetBackupTablesAsync(IAzureTableBackup backupSettings);
        Task<List<DynamicTableEntity>> GetEntitiesAsync(IAzureTableBackup backupSettings);
        Task<BackupResult> BackupEntitiesAsync(IAzureTableBackup backupSettings, List<DynamicTableEntity> entities);
        Task DeleteBackupTableAsync(IAzureTableBackup backupSettings, string tableName);
        Task AddBackupResultTrackerAsync(IAzureTableBackup backupSettings, BackupResult backupResult);
        Task<BackupResult> GetLastestBackupResultTrackerAsync(IAzureTableBackup backupSettings);
    }
}
