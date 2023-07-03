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
        Task<List<SyncJob>> GetSyncJobsAsync();
        Task<IEnumerable<SyncJob>> GetSyncJobsAsync(bool includeFutureJobs, params SyncStatus[] statusFilters);
        Task<SyncJob> GetSyncJobAsync(Guid syncJobId);
        Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus? status);
    }
}
