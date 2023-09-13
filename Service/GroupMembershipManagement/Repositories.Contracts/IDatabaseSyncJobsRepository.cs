// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseSyncJobsRepository
    {
        Task<SyncJob> GetSyncJobAsync(Guid syncJobId);
        IQueryable<SyncJob> GetSyncJobs(bool asNoTracking = false);
        Task<List<SyncJob>> GetSyncJobsAsync();
        Task<IEnumerable<SyncJob>> GetSyncJobsAsync(bool includeFutureJobs, params SyncStatus[] statusFilters);
		Task<int> GetSyncJobCountAsync(bool includeFutureJobs, params SyncStatus[] statusFilters); 
		Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status);
        Task UpdateSyncJobFromNotificationAsync(SyncJob job, SyncStatus status);
        Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status = null);
        Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs);
        Task BatchUpdateSyncJobsAsync(List<SyncJob> jobs);
    }
}
