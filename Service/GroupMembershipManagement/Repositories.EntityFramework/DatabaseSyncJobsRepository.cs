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

        public async Task<Guid> CreateSyncJobAsync(SyncJob job)
        {
            var entry = await _writeContext.Set<SyncJob>().AddAsync(job);
            await _writeContext.SaveChangesAsync();
            return entry.Entity.Id;
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

        public async Task<List<SyncJob>> GetSyncJobsByDestinationAsync(string destinationType)
        {
            return await _readContext.SyncJobs.FromSqlRaw<SyncJob>(@"SELECT * FROM [dbo].[SyncJobs] WHERE JSON_VALUE(Destination, '$[0].type') = {0}", destinationType).ToListAsync();
        }

        public async Task<SyncJob> GetSyncJobByObjectIdAsync(Guid objectId)
        {
            return await _readContext.SyncJobs.FromSqlRaw<SyncJob>(@"SELECT * FROM [dbo].[SyncJobs] WHERE JSON_VALUE(Destination, '$[0].value.objectId') = {0}", objectId.ToString()).FirstOrDefaultAsync();
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

		public async Task UpdateSyncJobStatusAsync(IEnumerable<SyncJob> jobs, SyncStatus? status)
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

                var jobWithOwners = await _writeContext.SyncJobs
                .Include(p => p.DestinationOwners)
                    .ThenInclude(owner => owner.SyncJobs)
                .SingleOrDefaultAsync(j => j.Id == job.Id);

                foreach (var owner in jobWithOwners.DestinationOwners)
                {
                    if (owner.SyncJobs.Count() < 2)
                    {
                        _writeContext.DestinationOwners.Remove(owner);
                    }
                }

                _writeContext.SyncJobs.Remove(jobWithOwners);
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