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

