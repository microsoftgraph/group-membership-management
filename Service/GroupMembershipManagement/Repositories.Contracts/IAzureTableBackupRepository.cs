// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.AzureMaintenance;
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts.InjectConfig;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAzureMaintenanceRepository : IAzureStorageBackupRepository
    {
        Task<List<DynamicTableEntity>> GetEntitiesAsync(IAzureMaintenance backupSettings);
        Task AddBackupResultTrackerAsync(IAzureMaintenance backupSettings, BackupResult backupResult);
        Task DeleteBackupTrackersAsync(IAzureMaintenance backupSettings, List<(string PartitionKey, string RowKey)> keys);
        Task<BackupResult> GetLastestBackupResultTrackerAsync(IAzureMaintenance backupSettings);
    }
}
