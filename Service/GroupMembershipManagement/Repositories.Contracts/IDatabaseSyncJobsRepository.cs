// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseSyncJobsRepository
    {
        Task InsertSyncJobsAsync(List<SyncJob> jobs);
        Task<SyncJob> GetSyncJobAsync(Guid syncJobId);
        Task<List<SyncJob>> GetSyncJobsAsync();
        Task<IEnumerable<SyncJob>> GetSyncJobsAsync(bool includeFutureJobs, params SyncStatus[] statusFilters);
        Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status);
        Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status = null);
        Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs);
        Task BatchUpdateSyncJobsAsync(List<SyncJob> jobs);
    }
}
