// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;
using System.Text.Json;

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
            return await _readContext.SyncJobs
                .Include(j => j.Group)
                .Include(j => j.Channel)
                .SingleOrDefaultAsync(job => job.Id == syncJobId);
        }
        
        public async Task<int> GetThresholdViolationsBySyncJobIdAsync(Guid syncJobId)
        {
            var syncJob = await _readContext.SyncJobs.SingleOrDefaultAsync(job => job.Id == syncJobId);
            return syncJob.ThresholdViolations;
        }
        public async Task<int> GetPeriodBySyncJobIdAsync(Guid syncJobId)
        {
            var syncJob = await _readContext.SyncJobs.SingleOrDefaultAsync(job => job.Id == syncJobId);
            return syncJob.Period;
        }
        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            return await _readContext.SyncJobs
                            .Include(j => j.Group)
                            .Include(j => j.Channel)
                            .ToListAsync();
        }

        public IQueryable<SyncJob> GetSyncJobs(bool asNoTracking = false)
        {
            return asNoTracking ?
                    _readContext.SyncJobs.Include(j => j.Group).Include(j => j.Channel).AsNoTracking()
                    : _readContext.SyncJobs.Include(j => j.Group).Include(j => j.Channel);
        }

        public async Task<List<SyncJob>> GetSyncJobsByDestinationAsync(string destinationType)
        {
            return await _readContext.SyncJobs
                           .Include(j => j.Group)
                           .Include(j => j.Channel)
                           .Where(job => job.MembershipType == destinationType)
                           .ToListAsync();
        }

        public async Task<SyncJob> GetSyncJobByObjectIdAsync(Guid objectId)
        {
            var syncJob = await _readContext.SyncJobs.FromSqlRaw<SyncJob>(@"SELECT s.*
                                FROM SyncJobs s
                                LEFT JOIN Groups g
                                    ON s.Id = g.SyncJobId
                                    AND s.MembershipType = 'GroupMembership'
                                    AND g.GroupId = {0}
                                LEFT JOIN TeamsChannels c
                                    ON s.Id = c.SyncJobId
                                    AND s.MembershipType = 'TeamsChannelMembership'
                                    AND c.GroupId = {0}
                                WHERE (g.SyncJobId IS NOT NULL AND c.SyncJobId IS NULL)
                                   OR (c.SyncJobId IS NOT NULL AND g.SyncJobId IS NULL)", objectId.ToString()).FirstOrDefaultAsync();
            return syncJob;
        }

        public async Task<IEnumerable<SyncJob>> GetSyncJobsAsync(bool includeFutureScheduledJobs, params SyncStatus[] statusFilters)
        {
            IQueryable<SyncJob> query = _readContext.SyncJobs
                                                    .Include(syncJob => syncJob.Group)
                                                    .Include(syncJob => syncJob.Channel);

            DateTime currentUtcTime = DateTime.UtcNow;
            query = query.Where(job => job.StartDate <= currentUtcTime);

            if (!includeFutureScheduledJobs)
            {
                query = query.Where(job => job.ScheduledDate <= currentUtcTime);
            }

            if (!statusFilters.Contains(SyncStatus.All))
            {
                var statuses = statusFilters.Select(x => x.ToString()).ToList();
                query = query.Where(job => statuses.Contains(job.Status));
            }

            return await query.ToListAsync();
        }

        public async Task<int> GetSyncJobCountAsync(params SyncStatus[] statusFilters)
        {
            IQueryable<SyncJob> query = _readContext.SyncJobs;

            DateTime currentUtcTime = DateTime.UtcNow;

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
                if (job.Group != null) _writeContext.Entry(job.Group).State = EntityState.Unchanged;
                if (job.Channel != null) _writeContext.Entry(job.Channel).State = EntityState.Unchanged;
                var entry = _writeContext.Set<SyncJob>().Add(job);
                entry.State = EntityState.Modified;
            }

            await _writeContext.SaveChangesAsync();
        }

        public async Task UpdateSyncJobFromNotificationAsync(SyncJob job, SyncStatus status)
        {
            if (job.Group != null) _writeContext.Entry(job.Group).State = EntityState.Unchanged;
            if (job.Channel != null) _writeContext.Entry(job.Channel).State = EntityState.Unchanged;
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

                if (jobWithOwners == null) continue;

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
        public async Task DeleteSyncJobAsync(SyncJob job)
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

            await _writeContext.SaveChangesAsync();
        }

        public async Task BatchUpdateSyncJobsAsync(List<SyncJob> jobs)
        {
            var existingJobs = await _writeContext.SyncJobs
                                                .Where(job => jobs.Select(j => j.Id).Contains(job.Id))
                                                .ToListAsync();
            foreach (var job in existingJobs)
            {
                var updatedJob = jobs.First(j => j.Id == job.Id);
                job.ScheduledDate = updatedJob.ScheduledDate;
            }

            await _writeContext.SaveChangesAsync();
        }

        public async Task BulkApproveSyncJobsAsync(List<string> syncJobIds)
        {
            var existingJobs = await _writeContext.SyncJobs
                .Where(job => syncJobIds.Contains(job.Id.ToString()))
                .ToListAsync();

            foreach (var job in existingJobs)
            {
                if (job.Status == SyncStatus.PendingReview.ToString())
                {
                    job.Status = SyncStatus.Idle.ToString();
                }
            }

            await _writeContext.SaveChangesAsync();
        }
    }
}