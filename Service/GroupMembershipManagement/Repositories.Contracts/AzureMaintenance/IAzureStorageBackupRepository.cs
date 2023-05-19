// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.AzureMaintenance;
using Services.Entities.Contracts;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Repositories.Contracts.AzureMaintenance
{
    public interface IAzureStorageBackupRepository
    {
        Task<BackupResult> BackupEntitiesAsync(IAzureMaintenanceJob maintenanceJob, List<ImmutableDictionary<string, object>> entities);
        Task<List<BackupEntity>> GetBackupsAsync(IAzureMaintenanceJob maintenanceJob);
        Task<bool> VerifyCleanupAsync(IAzureMaintenanceJob maintenanceJob, string tableName);
        Task CleanupAsync(IAzureMaintenanceJob maintenanceJob, string tableName);
    }
}
