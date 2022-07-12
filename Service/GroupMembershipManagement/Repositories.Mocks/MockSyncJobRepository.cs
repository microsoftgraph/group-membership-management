// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Entities;
using Microsoft.Azure.Cosmos.Table;
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

        // these aren't actually async, but this is the easiest way to get these to return IAsyncEnumerables
        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool applyFilters = true)
        {
            foreach (var job in ExistingSyncJobs.Values.Where(x => Enum.Parse<SyncStatus>(x.Status) == status || status == SyncStatus.All))
                yield return await Task.FromResult(job);
        }

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(IEnumerable<(string partitionKey, string rowKey)> jobIds)
        {
            foreach (var job in jobIds.Select(x => ExistingSyncJobs[x]))
                yield return await Task.FromResult(job);
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

        public IQueryable<SyncJob> GetJobsQuery(SyncStatus status = SyncStatus.All, bool includeFutureJobs = false, bool applyFilters = true)
        {
            throw new NotImplementedException();
        }

        public Task<TableSegmentBulkResult> GetSyncJobsAsync(IQueryable<SyncJob> jobsQuery, TableContinuationToken continuationToken)
        {
            throw new NotImplementedException();
        }

        public Task BatchUpdateSyncJobsAsync(IEnumerable<SyncJob> jobs)
        {
            throw new NotImplementedException();
        }

        public Task<TableSegmentBulkResult> GetSyncJobsAsync(IQueryable<SyncJob> jobsQuery, TableContinuationToken continuationToken, bool applyFilters = true)
        {
            throw new NotImplementedException();
        }

        public AsyncPageable<SyncJob> GetPageableQueryResultAsync(SyncStatus status = SyncStatus.All, bool includeFutureJobs = false)
        {
            throw new NotImplementedException();
        }

        public Task<TableSegmentBulkResult> GetSyncJobsSegmentAsync(AsyncPageable<SyncJob> pageableQueryResult, string continuationToken, bool applyFilters = true)
        {
            throw new NotImplementedException();
        }
    }
}
