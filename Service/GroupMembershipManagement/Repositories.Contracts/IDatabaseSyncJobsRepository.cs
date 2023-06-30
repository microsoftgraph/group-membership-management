// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseSyncJobsRepository
    {
        Task<List<SyncJob>> GetSyncJobsAsync();
        Task<IEnumerable<SyncJob>> GetSyncJobsAsync(bool includeFutureJobs, params SyncStatus[] statusFilters);
        Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status);
    }
}
