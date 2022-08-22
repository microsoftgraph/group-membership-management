// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ISyncJobRepository
    {
        AsyncPageable<SyncJob> GetPageableQueryResult(SyncStatus status = SyncStatus.All, bool includeFutureJobs = false);
        Task<TableSegmentBulkResult<DistributionSyncJob>> GetSyncJobsSegmentAsync(AsyncPageable<SyncJob> pageableQueryResult, string continuationToken, bool applyFilters = true);
        Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey);
        IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool applyFilters = true);
        IAsyncEnumerable<SyncJob> GetSyncJobsAsync(IEnumerable<(string partitionKey, string rowKey)> jobIds);
        Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status);
        Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status = null);
        Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> jobs);
    }
}
