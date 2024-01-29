// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ISyncJobRepository
    {
        IAsyncEnumerable<SyncJob> GetSyncJobsAsync(bool includeFutureJobs = false, params SyncStatus[] statusFilters);
        Task<Page<SyncJob>> GetPageableQueryResultAsync(bool includeFutureJobs = false, int? pageSize = null, params SyncStatus[] statusFilters);
        Task<Page<SyncJob>> GetSyncJobsSegmentAsync(string query, string continuationToken, int batchSize);
        Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey);
        IAsyncEnumerable<SyncJob> GetSpecificSyncJobsAsync();
        Task<SyncJob> GetSyncJobByObjectIdAsync(Guid objectId);
        Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status);
        Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status = null);
        Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> jobs);
        Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs);
    }
}
