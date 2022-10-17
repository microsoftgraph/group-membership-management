// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.AzureMaintenance;
using Microsoft.Azure.Cosmos.Table;
using Services.Entities;
using Services.Entities.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts.AzureMaintenance
{
    public interface IAzureTableBackupRepository : IAzureStorageBackupRepository
    {
        Task<List<DynamicTableEntity>> GetEntitiesAsync(IAzureMaintenanceJob backupSettings);
        Task AddBackupResultTrackerAsync(IAzureMaintenanceJob backupSettings, BackupResult backupResult);
        Task DeleteBackupTrackersAsync(IAzureMaintenanceJob backupSettings, List<(string PartitionKey, string RowKey)> keys);
        Task<BackupResult> GetLastestBackupResultTrackerAsync(IAzureMaintenanceJob backupSettings);
    }
}
