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
        private readonly DbContextOptions<GMMContext> _contextOptions;

        public DatabaseSyncJobsRepository(GMMContext gmmContext, DbContextOptions<GMMContext> gmmContextOptions)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
            _contextOptions = gmmContextOptions ?? throw new ArgumentNullException(nameof(gmmContextOptions));
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

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status)
        {
            var entity = await _context.SyncJobs.FirstOrDefaultAsync(item => item.Id == job.Id);
            entity.Status = Enum.GetName(typeof(SyncStatus), status);
            entity.LastSuccessfulStartTime = job.LastSuccessfulStartTime;
            entity.LastRunTime = job.LastRunTime;
            entity.RunId = job.RunId;
            entity.StartDate = job.StartDate;
            await _context.SaveChangesAsync();
        }
    }
}