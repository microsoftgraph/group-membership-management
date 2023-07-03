// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.SyncJobs.Tests
{
    public class MockDatabaseSyncJobRepository : IDatabaseSyncJobsRepository
    {
        public List<SyncJob> Jobs { get; set; } = new List<SyncJob>();

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            return await Task.FromResult(Jobs);
        }

        public async Task<IEnumerable<SyncJob>> GetSyncJobsAsync(bool includeFutureJobs, params SyncStatus[] statusFilters)
        {
            var jobs = Jobs.Where(x => (x.StartDate <= DateTime.UtcNow)
                                        && (DateTime.UtcNow - x.LastRunTime > TimeSpan.FromHours(x.Period))
                                        && (x.Status == SyncStatus.Idle.ToString() || x.Status == SyncStatus.InProgress.ToString() || x.Status == SyncStatus.StuckInProgress.ToString())).ToList();

            return await Task.FromResult(jobs);
        }

        public async Task<SyncJob> GetSyncJobAsync(Guid syncJobId)
        {
            var job = Jobs.FirstOrDefault(x => x.Id == syncJobId);
            return await Task.FromResult(job);
        }

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus? status)
        {
            var dbJob = Jobs.FirstOrDefault(x => x.Id == job.Id);
            if (dbJob != null)
            {
                dbJob.Status = status?.ToString();
            }
            await Task.CompletedTask;
        }
    }
}