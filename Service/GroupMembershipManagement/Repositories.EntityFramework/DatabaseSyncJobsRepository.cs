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
        private readonly GMMContext _context;

        public DatabaseSyncJobsRepository(GMMContext gmmContext)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
        }

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            return await _context.SyncJobs.ToListAsync();
        }

        public async Task<IEnumerable<SyncJob>> GetSyncJobsAsync(bool includeFutureJobs, params SyncStatus[] statusFilters)
        {
            IQueryable<SyncJob> query = _context.SyncJobs;

            if (statusFilters.Contains(SyncStatus.All))
            {
                if (!includeFutureJobs)
                {
                    DateTime currentUtcTime = DateTime.UtcNow;
                    query = query.Where(job => job.StartDate <= currentUtcTime);
                }

                return await query.ToListAsync();
            }

            if (!includeFutureJobs)
            {
                var statuses = statusFilters.Select(x => x.ToString()).ToList();
                DateTime currentUtcTime = DateTime.UtcNow;
                query = query.Where(job => job.StartDate <= currentUtcTime && statuses.Contains(job.Status));
            }

            return await query.ToListAsync();
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
                var entry = _context.Set<SyncJob>().Add(job);
                entry.State = EntityState.Modified;
            }

            await _context.SaveChangesAsync();
        }
    }
}