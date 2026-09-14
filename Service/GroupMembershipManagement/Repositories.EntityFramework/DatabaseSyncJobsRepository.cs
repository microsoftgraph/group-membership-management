// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using System.Data.SqlTypes;
using Models;
using Models.Dashboard;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;
using System.Text.Json;

namespace Repositories.EntityFramework
{
    public class DatabaseSyncJobsRepository : IDatabaseSyncJobsRepository
    {
        // A job that has never run still carries a sentinel timestamp. The 1900-01-01
        // threshold covers both the C# default (SqlDateTime.MinValue, 1753-01-01) and the
        // SQL column default (1601-01-01).
        private static readonly DateTime NeverRunSentinel = new DateTime(1900, 1, 1);

        private const string OnboardingRequestType = "Onboarding";
        private const string UpdateRequestType = "Update";

        private static readonly string[] InProgressStatuses =
        {
            nameof(SyncStatus.InProgress),
            nameof(SyncStatus.StuckInProgress),
            nameof(SyncStatus.ErroredDueToStuckInProgress)
        };

        // Healthy or transitional statuses that are never anomalous.
        private static readonly string[] OperationalStatuses =
        {
            nameof(SyncStatus.Idle),
            nameof(SyncStatus.InProgress),
            nameof(SyncStatus.PendingReview)
        };

        // Anything outside this set is reported as anomalous on the dashboard. Purge-eligible
        // statuses are excluded because AzureMaintenance already tracks, warns on, and cleans
        // them up, so surfacing them here would double-report an already-managed lifecycle.
        // Sourced from PurgeEligibleStatuses so the dashboard stays in step as that list changes.
        private static readonly string[] NonAnomalousStatuses =
            OperationalStatuses
                .Concat(PurgeEligibleStatuses.All.Select(status => status.ToString()))
                .ToArray();

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

        public async Task<List<Guid>> GetDestinationOwnerIdsAsync(Guid syncJobId)
        {
            return await _readContext.SyncJobs
                .Where(job => job.Id == syncJobId)
                .SelectMany(job => job.DestinationOwners)
                .Select(owner => owner.ObjectId)
                .ToListAsync();
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

        public async Task<SyncJob> GetSyncJobByTeamIdAndChannelIdAsync(Guid teamId, string channelId)
        {
            // Returns any existing TeamsChannel SyncJob row for the exact (TeamId, ChannelId) pair.
            // "Active" = a row exists regardless of Status (including CustomerPaused). Deleted jobs are
            // hard-deleted (their TeamsChannels row is removed), so no soft-delete filter is required.
            if (string.IsNullOrEmpty(channelId))
                return null;

            return await _readContext.SyncJobs
                .Include(j => j.Channel)
                .Where(j => j.MembershipType == "TeamsChannelMembership"
                            && j.Channel != null
                            && j.Channel.GroupId == teamId
                            && j.Channel.ChannelId == channelId)
                .FirstOrDefaultAsync();
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

        public async Task<int> BulkApproveSyncJobsAsync(List<string> syncJobIds)
        {
            var existingJobs = await _writeContext.SyncJobs
                .Where(job => syncJobIds.Contains(job.Id.ToString()))
                .ToListAsync();

            int updatedToIdleCount = 0;

            foreach (var job in existingJobs)
            {
                if (job.Status == SyncStatus.PendingReview.ToString())
                {
                    job.Status = SyncStatus.Idle.ToString();
                    updatedToIdleCount++;
                }
            }

            await _writeContext.SaveChangesAsync();

            return updatedToIdleCount;
        }

        public async Task<int> BulkResetJobStatusAsync(SyncStatus fromStatus, SyncStatus toStatus, CancellationToken cancellationToken = default)
        {
            return await _writeContext.SyncJobs
                .Where(j => j.Status == fromStatus.ToString())
                .ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.Status, toStatus.ToString()),
                    cancellationToken);
        }

        public async Task<SyncJob?> ClaimSyncJobAsync(Guid jobId, Guid? runId, int period, string targetStatus)
        {
            var idleStatus = SyncStatus.Idle.ToString();
            var inProgressStatus = SyncStatus.InProgress.ToString();
            var stuckStatus = SyncStatus.StuckInProgress.ToString();
            var transientStatus = SyncStatus.TransientError.ToString();
            var cutoffTime = DateTime.UtcNow.AddHours(-period);

            var rows = await _writeContext.SyncJobs
                .Where(j => j.Id == jobId &&
                    (j.Status == idleStatus
                     || j.Status == transientStatus
                     || (j.Status == inProgressStatus && j.LastSuccessfulStartTime < cutoffTime)))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, targetStatus)
                    .SetProperty(j => j.RunId, runId)
                    .SetProperty(j => j.LastSuccessfulStartTime, DateTime.UtcNow)
                    .SetProperty(j => j.LastRunTime, j => targetStatus == stuckStatus ? DateTime.UtcNow : j.LastRunTime));

            if (rows == 0)
                return null;

            return await _writeContext.SyncJobs
                .Include(j => j.Group)
                .Include(j => j.Channel)
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId);
        }

        public async Task UpdateSyncJobDestinationAsync(Guid jobId, string destination)
        {
            await _writeContext.SyncJobs
                .Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Destination, destination));
        }

        public async Task<DashboardSummary> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var pendingReviewStatus = nameof(SyncStatus.PendingReview);

            // Correlated TOP 1 against the already-filtered PendingReview jobs. Ranking the
            // whole SyncJobChanges table with a window function first would force a full scan
            // because the status predicate cannot be pushed through the window.
            var pendingReviewRows = await _readContext.SyncJobs
                .AsNoTracking()
                .Where(job => job.Status == pendingReviewStatus)
                .Select(job => new
                {
                    IsOnboarding = job.LastRunTime <= NeverRunSentinel,
                    LatestChangeTime = _readContext.SyncJobChanges
                        .Where(change => change.SyncJobId == job.Id)
                        .OrderByDescending(change => change.ChangeTime)
                        .ThenByDescending(change => change.Id)
                        .Select(change => (DateTime?)change.ChangeTime)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var inProgressRows = await _readContext.SyncJobs
                .AsNoTracking()
                .Where(job => InProgressStatuses.Contains(job.Status))
                .Select(job => new { job.Status, job.LastSuccessfulStartTime })
                .ToListAsync(cancellationToken);

            var anomalousRows = await _readContext.SyncJobs
                .AsNoTracking()
                .Where(job => !NonAnomalousStatuses.Contains(job.Status))
                .Select(job => new { job.Status, job.LastSuccessfulRunTime, job.Period })
                .ToListAsync(cancellationToken);

            return new DashboardSummary
            {
                QueriedAtUtc = now,
                PendingReview = pendingReviewRows
                    .GroupBy(row => row.IsOnboarding ? OnboardingRequestType : UpdateRequestType)
                    .Select(group => new PendingReviewSummaryRow
                    {
                        RequestType = group.Key,
                        Count = group.Count(),
                        OldestChangeTime = group.Min(row => row.LatestChangeTime)
                    })
                    .OrderBy(row => row.RequestType)
                    .ToList(),

                InProgress = inProgressRows
                    .GroupBy(row => row.Status)
                    .Select(group => new InProgressStatusRow
                    {
                        Status = group.Key,
                        Count = group.Count(),
                        OldestStartTime = group
                            .Where(row => row.LastSuccessfulStartTime >= NeverRunSentinel)
                            .Min(row => (DateTime?)row.LastSuccessfulStartTime)
                    })
                    .OrderBy(row => row.Status)
                    .ToList(),

                AnomalousStatuses = anomalousRows
                    .GroupBy(row => row.Status)
                    .Select(group => new AnomalousStatusRow
                    {
                        Status = group.Key,
                        Count = group.Count(),
                        MaxCyclesMissed = group
                            .Where(row => row.LastSuccessfulRunTime >= NeverRunSentinel && row.Period > 0)
                            .Select(row => (int?)((int)(now - row.LastSuccessfulRunTime).TotalHours / row.Period))
                            .Max(),
                        OldestLastSuccessfulRun = group
                            .Where(row => row.LastSuccessfulRunTime >= NeverRunSentinel)
                            .Min(row => (DateTime?)row.LastSuccessfulRunTime),
                        NeverSucceededCount = group.Count(row => row.LastSuccessfulRunTime < NeverRunSentinel)
                    })
                    .OrderByDescending(row => row.Count)
                    .ThenBy(row => row.Status)
                    .ToList()
            };
        }
    }
}
