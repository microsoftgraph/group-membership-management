// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ISyncJobRepository
    {
        IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool includeDisabled = false);
        IAsyncEnumerable<SyncJob> GetSyncJobsAsync(IEnumerable<(string partitionKey, string rowKey)> jobIds);
        Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey);
        Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status);
    }
}
