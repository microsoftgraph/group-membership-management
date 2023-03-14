// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Entities;
using Models;
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

        public async Task<TableSegmentBulkResult<SyncJob>> GetSyncJobsSegmentAsync(AsyncPageable<SyncJob> pageableQueryResult, string continuationToken, int batchSize, bool applyFilters = true)
        {
            var jobs = Jobs.Where(x => (x.StartDate <= DateTime.UtcNow) 
                                        && (DateTime.UtcNow - x.LastRunTime > TimeSpan.FromHours(x.Period))
                                        && (x.Status == SyncStatus.Idle.ToString() || x.Status == SyncStatus.InProgress.ToString() || x.Status == SyncStatus.StuckInProgress.ToString()));
           
 

            return new TableSegmentBulkResult<SyncJob>
            {
                Results = new List<SyncJob>(jobs)
            };

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

        public async IAsyncEnumerable<SyncJob> GetSpecificSyncJobsAsync()
        {
            var jobs = Jobs.Where(x => x.StartDate <= DateTime.UtcNow);

            jobs = jobs.Where(x => x.Status != SyncStatus.Idle.ToString() &&
                                   x.Status != SyncStatus.InProgress.ToString() &&
                                   x.Status != SyncStatus.Error.ToString());

            foreach (var job in await Task.FromResult(jobs))
            {
                yield return job;
            }
        }

        public Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs)
        {
            throw new NotImplementedException();
        }

        public AsyncPageable<SyncJob> GetPageableQueryResult(bool includeFutureJobs = false, params SyncStatus[] statusFilters)
        {
            return null;
        }
    }
}