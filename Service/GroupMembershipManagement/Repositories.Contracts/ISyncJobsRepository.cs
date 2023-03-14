// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Entities;
using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ISyncJobRepository
    {
        public AsyncPageable<SyncJob> GetPageableQueryResult(bool includeFutureJobs = false, params SyncStatus[] statusFilters);
        Task<TableSegmentBulkResult<SyncJob>> GetSyncJobsSegmentAsync(AsyncPageable<SyncJob> pageableQueryResult, string continuationToken, int batchSize, bool applyFilters = true);
        Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey);
        IAsyncEnumerable<SyncJob> GetSpecificSyncJobsAsync();
        Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status);
        Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status = null);
        Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> jobs);
        Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs);
    }
}
