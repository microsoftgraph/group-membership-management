// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockSyncJobRepository : ISyncJobRepository
    {
        public Dictionary<(string, string), SyncJob> ExistingSyncJobs = new Dictionary<(string, string), SyncJob>();

        public async Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey)
        {
            var job = ExistingSyncJobs.ContainsKey((partitionKey, rowKey)) ? ExistingSyncJobs[(partitionKey, rowKey)] : null;
            return await Task.FromResult(job);
        }

        public Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status)
        {
            foreach (var job in jobs)
                job.Status = status.ToString();
            return Task.CompletedTask;
        }

        public Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status)
        {
            return Task.CompletedTask;
        }

        public Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> jobs)
        {
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<SyncJob> GetSpecificSyncJobsAsync()
        {
            foreach (var job in ExistingSyncJobs.Values.Where(x => Enum.Parse<SyncStatus>(x.Status) == SyncStatus.CustomerPaused))
                yield return await Task.FromResult(job);
        }

        public Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<SyncJob> GetSyncJobsAsync(bool includeFutureJobs = false, params SyncStatus[] statusFilters)
        {
            throw new NotImplementedException();
        }

        public Task<Page<SyncJob>> GetPageableQueryResultAsync(bool includeFutureJobs = false, int? pageSize = null, params SyncStatus[] statusFilters)
        {
            return Task.FromResult(new Page<SyncJob>());
        }

        public Task<Page<SyncJob>> GetSyncJobsSegmentAsync(string query, string continuationToken, int batchSize, bool applyFilters = true)
        {
            return Task.FromResult(new Page<SyncJob>());
        }
    }
}
