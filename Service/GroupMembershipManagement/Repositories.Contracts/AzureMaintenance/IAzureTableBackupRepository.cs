// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Data.Tables;
using Entities.AzureMaintenance;
using Models;
using Models.AzureMaintenance;
using Services.Entities.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts.AzureMaintenance
{
    public interface IAzureTableBackupRepository : IAzureStorageBackupRepository
    {
        Task<List<TableEntity>> GetEntitiesAsync(IAzureMaintenanceJob backupSettings);
        Task AddBackupResultTrackerAsync(IAzureMaintenanceJob backupSettings, BackupResult backupResult);
        Task DeleteBackupTrackersAsync(IAzureMaintenanceJob backupSettings, List<(string PartitionKey, string RowKey)> keys);
        Task<BackupResult> GetLatestBackupResultTrackerAsync(IAzureMaintenanceJob backupSettings);
        Task<int> BackupInactiveJobsAsync(List<SyncJob> syncJobs);
        Task<List<BackupTable>> GetInactiveBackupsAsync();
        Task DeleteBackupTableAsync(string tableName);
    }
}
