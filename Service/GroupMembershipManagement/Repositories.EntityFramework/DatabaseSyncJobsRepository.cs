// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseSyncJobsRepository : IDatabaseSyncJobsRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public DatabaseSyncJobsRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<SyncJob> GetSyncJobAsync(Guid syncJobId)
        {
            return await _readContext.SyncJobs.SingleOrDefaultAsync(job => job.Id == syncJobId);
        }

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            return await _readContext.SyncJobs.ToListAsync();
        }

        public IQueryable<SyncJob> GetSyncJobs(bool asNoTracking = false)
        {
            return asNoTracking ?
                    _readContext.SyncJobs.AsNoTracking()
                    : _readContext.SyncJobs;
        }

        public async Task<IEnumerable<SyncJob>> GetSyncJobsAsync(bool includeFutureJobs, params SyncStatus[] statusFilters)
        {
            IQueryable<SyncJob> query = _readContext.SyncJobs;

            if (!includeFutureJobs)
            {
                DateTime currentUtcTime = DateTime.UtcNow;
                query = query.Where(job => job.StartDate <= currentUtcTime);
            }

            if (!statusFilters.Contains(SyncStatus.All))
            {
                var statuses = statusFilters.Select(x => x.ToString()).ToList();
                query = query.Where(job => statuses.Contains(job.Status));
            }

            return await query.ToListAsync();
        }

        public async Task<int> GetSyncJobCountAsync(bool includeFutureJobs, params SyncStatus[] statusFilters)
        {
            IQueryable<SyncJob> query = _readContext.SyncJobs;

            if (!includeFutureJobs)
            {
                DateTime currentUtcTime = DateTime.UtcNow;
                query = query.Where(job => job.StartDate <= currentUtcTime);
            }

            if (!statusFilters.Contains(SyncStatus.All))
            {
                var statuses = statusFilters.Select(x => x.ToString()).ToList();
                query = query.Where(job => statuses.Contains(job.Status));
            }

            return await query.CountAsync();
        }

		public async Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus status)
        {
            await UpdateSyncJobsAsync(jobs, status: status);
        }

        public async Task UpdateSyncJobsAsync(IEnumerable<SyncJob> jobs, SyncStatus? status = null)
        {
            foreach (var job in jobs)
            {
                if (status != null)
                {
                    job.Status = status.ToString();
                }
                var entry = _writeContext.Set<SyncJob>().Add(job);
                entry.State = EntityState.Modified;
            }

            await _writeContext.SaveChangesAsync();
        }

        public async Task UpdateSyncJobFromNotificationAsync(SyncJob job, SyncStatus status)
        {
            var entry = _writeContext.Set<SyncJob>().Add(job);
            job.Status = status.ToString();
            entry.State = EntityState.Modified;
            await _writeContext.SaveChangesAsync();
        }

        public async Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs)
        {
            foreach (var job in jobs)
            {
                var entry = _writeContext.Set<SyncJob>().Add(job);
                entry.State = EntityState.Deleted;
            }

            await _writeContext.SaveChangesAsync();
        }

        public async Task BatchUpdateSyncJobsAsync(List<SyncJob> jobs)
        {
            foreach (var job in jobs)
            {
                _writeContext.Set<SyncJob>().Attach(job);
                _writeContext.Entry(job).Property(x => x.StartDate).IsModified = true;
            }

            await _writeContext.SaveChangesAsync();
        }
    }
}