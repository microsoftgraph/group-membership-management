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

        public async Task<Guid> CreateSyncJobAsync(SyncJob job)
        {
            job.Id = Guid.NewGuid();
            Jobs.Add(job);
            return await Task.FromResult(job.Id);
        }

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            return await Task.FromResult(Jobs);
        }

        public async Task<IEnumerable<SyncJob>> GetSyncJobsAsync(bool includeFutureScheduledJobs = true, params SyncStatus[] statusFilters)
        {
            var jobs = Jobs.Where(x => (x.StartDate <= DateTime.UtcNow)
                                        && (DateTime.UtcNow - x.LastRunTime > TimeSpan.FromHours(x.Period))
                                        && (x.Status == SyncStatus.Idle.ToString() || x.Status == SyncStatus.InProgress.ToString() || x.Status == SyncStatus.StuckInProgress.ToString())).ToList();

            return await Task.FromResult(jobs);
        }
		public async Task<int> GetSyncJobCountAsync(params SyncStatus[] statusFilters)
		{
			IEnumerable<SyncJob> jobs = Jobs;

            if (statusFilters.Contains(SyncStatus.All))
			{
				return await Task.FromResult(jobs.Count());
			}

			var statuses = statusFilters.Select(x => x.ToString()).ToList();
			jobs = jobs.Where(job => statuses.Contains(job.Status));

			return await Task.FromResult(jobs.Count());
		}

		public async Task<SyncJob> GetSyncJobAsync(Guid syncJobId)
        {
            var job = Jobs.FirstOrDefault(x => x.Id == syncJobId);
            return await Task.FromResult(job);
        }

        public async Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus? status)
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

        public Task BatchUpdateSyncJobsAsync(List<SyncJob> jobs)
        {
            throw new NotImplementedException();
        }

        public Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs)
        {
            throw new NotImplementedException();
        }

        public Task DeleteSyncJobAsync(SyncJob job)
        {
            throw new NotImplementedException();
        }

        public IQueryable<SyncJob> GetSyncJobs(bool asNoTracking = false)
        {
            throw new NotImplementedException();
        }

        public async Task UpdateSyncJobFromNotificationAsync(SyncJob job, SyncStatus status)
        {
            var dbJob = Jobs.FirstOrDefault(x => x.Id == job.Id);
            if (dbJob != null)
            {
                dbJob.Status = status.ToString();
            }
            await Task.CompletedTask;
        }

        public Task<List<SyncJob>> GetSyncJobsByDestinationAsync(string destinationType)
        {
            throw new NotImplementedException();
        }

        public Task<SyncJob> GetSyncJobByObjectIdAsync(Guid objectId)
        {
            throw new NotImplementedException();
        }

        public Task InsertSyncJobAsync(SyncJob job)
        {
            throw new NotImplementedException();
        }
    }
}