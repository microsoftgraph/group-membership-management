// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts.InjectConfig;
using Services.Entities.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IAzureMaintenanceService
    {
        Task RunBackupServiceAsync(IAzureMaintenanceJob maintenanceJob);
        Task<List<IReviewAndDeleteRequest>> RetrieveBackupsAsync(IAzureMaintenanceJob maintenanceJob);
        Task<bool> ReviewAndDeleteAsync(IAzureMaintenanceJob maintenanceJob, string tableName);
        Task<List<SyncJob>> GetSyncJobsAsync();
        Task<int> BackupInactiveJobsAsync(List<SyncJob> syncJobs);
        Task RemoveInactiveJobsAsync(IEnumerable<SyncJob> jobs);
        Task<List<string>> RemoveBackupsAsync();
        Task<string> GetGroupNameAsync(Guid groupId);
        Task SendEmailAsync(SyncJob job, string groupName);
    }
}
