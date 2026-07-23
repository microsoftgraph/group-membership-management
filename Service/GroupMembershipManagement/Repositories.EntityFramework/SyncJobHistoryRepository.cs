// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class SyncJobHistoryRepository : ISyncJobHistoryRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public SyncJobHistoryRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task CreateAsync(SyncJobHistory jobHistory)
        {
            if (jobHistory == null) throw new ArgumentNullException(nameof(jobHistory));

            await _writeContext.SyncJobHistory.AddAsync(jobHistory);
            await _writeContext.SaveChangesAsync();
        }

        public async Task<List<SyncJobHistory>> GetBySyncJobIdAsync(Guid syncJobId, int pageSize = 50, int pageNumber = 1)
        {
            // Exclude InProgress records - UI should only show completed sync results
            return await _readContext.SyncJobHistory
                .Where(h => h.SyncJobId == syncJobId && h.Status != SyncStatus.InProgress.ToString())
                .OrderByDescending(h => h.UpdatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<SyncJobHistory?> GetByRunIdAsync(Guid runId)
        {
            return await _readContext.SyncJobHistory
                .FirstOrDefaultAsync(h => h.RunId == runId);
        }

        public async Task<SyncJobHistory?> GetMostRecentAsync(Guid syncJobId)
        {
            return await _readContext.SyncJobHistory
                .Where(h => h.SyncJobId == syncJobId)
                .OrderByDescending(h => h.UpdatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task UpdateAsync(SyncJobHistory jobHistory)
        {
            if (jobHistory == null) throw new ArgumentNullException(nameof(jobHistory));

            _writeContext.SyncJobHistory.Update(jobHistory);
            await _writeContext.SaveChangesAsync();
        }

        public async Task<int> SaveAdfRunIdAsync(Guid runId, Guid? adfRunId)
        {
            // A valid RunId is required to key the history row; without one we cannot target a write.
            if (runId == Guid.Empty) return 0;

            // Nothing to persist when there is no ADF run identifier. Never write null here: doing so
            // would erase a pointer that a prior (or terminal) writer may already have stored for this run.
            if (!adfRunId.HasValue) return 0;

            // Field-scoped atomic UPDATE on the primary (write) context. Translates to a single
            // UPDATE SyncJobHistory SET AdfRunId=@p, UpdatedAt=@now WHERE RunId=@runId, so it never reads a
            // replica and touches ONLY AdfRunId/UpdatedAt - it cannot revert a terminal status. JobTrigger
            // creates the run's InProgress history row at claim time, so the row already exists by the time
            // SqlMembershipObtainer stashes the ADF run identifier: this update is the normal path. We never
            // insert here, so this can never create a duplicate row. If the row is genuinely missing (0 rows
            // updated - e.g. an environment where the JobTrigger fix is not yet deployed) the caller can
            // surface a warning; that transitional gap is preferable to inserting duplicate history rows
            // (RunId is not uniquely constrained in the database).
            return await _writeContext.SyncJobHistory
                .Where(h => h.RunId == runId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(h => h.AdfRunId, adfRunId)
                    .SetProperty(h => h.UpdatedAt, DateTime.UtcNow));
        }

        public async Task<int> DeleteOlderThanAsync(DateTime cutoffDate)
        {
            const int batchSize = 5000;
            var originalTimeout = _writeContext.Database.GetCommandTimeout();
            try
            {
                _writeContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

                var totalDeleted = 0;
                int deletedInBatch;

                do
                {
                    deletedInBatch = await _writeContext.SyncJobHistory
                        .Where(h => h.UpdatedAt < cutoffDate)
                        .Take(batchSize)
                        .ExecuteDeleteAsync();

                    totalDeleted += deletedInBatch;
                }
                while (deletedInBatch == batchSize);

                return totalDeleted;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to delete job history records with cutoff date: {cutoffDate:yyyy-MM-dd}",
                    ex);
            }
            finally
            {
                _writeContext.Database.SetCommandTimeout(originalTimeout);
            }
        }
    }
}

