// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Polly;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockDatabaseSyncJobRepository : IDatabaseSyncJobsRepository
    {
        public List<SyncJob> Jobs { get; set; } = new List<SyncJob>();
        
        public async Task AddSyncJobAsync(SyncJob job)
        {
            Jobs.Add(job);
            await Task.CompletedTask;
        }

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

        public async Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status)
        {
            foreach (var job in jobs)
            {
                var dbJob = Jobs.FirstOrDefault(x => x.Id == job.Id);
                if (dbJob != null)
                {
                    dbJob.Status = status.ToString();
                }
                await Task.CompletedTask;
            }
        }

        public Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status = null)
        {
            throw new NotImplementedException();
        }
    }
}