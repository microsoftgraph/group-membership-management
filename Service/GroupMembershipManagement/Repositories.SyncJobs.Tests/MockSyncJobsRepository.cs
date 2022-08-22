// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.SyncJobs.Tests
{
    public class MockSyncJobRepository : ISyncJobRepository
    {
        public List<SyncJob> Jobs { get; set; } = new List<SyncJob>();

        public async Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey)
        {
            var job = Jobs.FirstOrDefault(x => x.PartitionKey == partitionKey && x.RowKey == rowKey);
            return await Task.FromResult(job);
        }

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool applyFilters = true)
        {
            var jobs = Jobs.Where(x => x.StartDate <= DateTime.UtcNow);

            if (status != SyncStatus.All)
            {
                jobs = jobs.Where(x => x.Status == status.ToString());
            }

            if (applyFilters)
            {
                jobs = jobs.Where(x => DateTime.UtcNow - x.LastRunTime > TimeSpan.FromHours(x.Period));
            }

            foreach (var job in await Task.FromResult(jobs))
            {
                yield return job;
            }
        }

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(IEnumerable<(string partitionKey, string rowKey)> jobIds)
        {
            foreach (var (partitionKey, rowKey) in jobIds)
            {
                var job = Jobs.Single(x => x.PartitionKey == partitionKey && x.RowKey == rowKey);
                yield return await Task.FromResult(job);
            }
        }

        public async Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status)
        {
            foreach (var job in jobs)
            {
                var dbJob = Jobs.Single(x => x.PartitionKey == job.PartitionKey && x.RowKey == job.RowKey);
                dbJob.Status = status.ToString();
            }

            await Task.CompletedTask;
        }

        public Task<TableSegmentBulkResult<DistributionSyncJob>> GetSyncJobsSegmentAsync(AsyncPageable<SyncJob> pageableQueryResult, string continuationToken, bool applyFilters = true)
        {
            throw new NotImplementedException();
        }

        public Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status = null)
        {
            throw new NotImplementedException();
        }

        public Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> jobs)
        {
            throw new NotImplementedException();
        }

        public AsyncPageable<SyncJob> GetPageableQueryResult(SyncStatus status = SyncStatus.All, bool includeFutureJobs = false)
        {
            throw new NotImplementedException();
        }
    }
}