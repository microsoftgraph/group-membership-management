// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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

        public async IAsyncEnumerable<SyncJob> GetSyncJobsAsync(SyncStatus status = SyncStatus.All, bool includeDisabled = false)
        {
            var jobs = Jobs.Where(x => x.StartDate <= DateTime.UtcNow);

            if (status != SyncStatus.All)
            {
                jobs = jobs.Where(x => x.Status == status.ToString());
            }

            if (!includeDisabled)
            {
                jobs = jobs.Where(x => x.Enabled);
            }

            jobs = jobs.Where(x => DateTime.UtcNow - x.LastRunTime > TimeSpan.FromHours(x.Period));

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
    }
}

