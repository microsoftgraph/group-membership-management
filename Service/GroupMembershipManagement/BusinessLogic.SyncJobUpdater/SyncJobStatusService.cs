// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace BusinessLogic.SyncJobUpdater
{
    public class SyncJobStatusService : ISyncJobStatusService
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;

        public SyncJobStatusService(
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ISyncJobHistoryRepository syncJobHistoryRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository;
            _syncJobHistoryRepository = syncJobHistoryRepository;
        }

        public async Task UpdateJobStatusAsync(SyncJob job, SyncStatus? status, SyncJobHistory? history = null, string? functionName = null)
        {
            await _databaseSyncJobsRepository.UpdateSyncJobStatusAsync(new[] { job }, status);

            // Some callers use this method to persist SyncJob field updates without changing status.
            // In that case we must not write a SyncJobHistory record with a default/incorrect status.
            if (!status.HasValue && history == null)
            {
                return;
            }

            if (history == null)
            {
                var now = DateTime.UtcNow;
                history = new SyncJobHistory
                {
                    SyncJobId = job.Id,
                    RunId = job.RunId ?? Guid.Empty,
                    Status = status.Value.ToString(),
                    UpdatedByFunction = functionName,
                    StartTime = job.LastRunTime,
                    CreatedAt = now,
                    UpdatedAt = now
                };
            }

            await CreateOrUpdateJobHistoryAsync(history);
        }

        public async Task CreateOrUpdateJobHistoryAsync(SyncJobHistory history)
        {
            var existingHistory = await _syncJobHistoryRepository.GetByRunIdAsync(history.RunId);

            if (existingHistory != null)
            {
                existingHistory.StartTime = history.StartTime ?? existingHistory.StartTime;
                existingHistory.EndTime = history.EndTime ?? existingHistory.EndTime;
                existingHistory.Status = history.Status;
                existingHistory.UsersAdded = history.UsersAdded ?? existingHistory.UsersAdded;
                existingHistory.UsersRemoved = history.UsersRemoved ?? existingHistory.UsersRemoved;
                existingHistory.ThresholdViolations = history.ThresholdViolations ?? existingHistory.ThresholdViolations;
                existingHistory.UpdatedByFunction = !string.IsNullOrEmpty(history.UpdatedByFunction) ? history.UpdatedByFunction : existingHistory.UpdatedByFunction;
                existingHistory.UpdatedAt = DateTime.UtcNow;

                if (history.StartTime.HasValue && !existingHistory.StartTime.HasValue)
                {
                    existingHistory.StartTime = history.StartTime;
                }

                if (existingHistory.StartTime.HasValue && existingHistory.EndTime.HasValue)
                {
                    existingHistory.Duration = (int)(existingHistory.EndTime.Value - existingHistory.StartTime.Value).TotalSeconds;
                }

                await _syncJobHistoryRepository.UpdateAsync(existingHistory);
            }
            else
            {

                if (history.StartTime.HasValue && history.EndTime.HasValue)
                {
                    history.Duration = (int)(history.EndTime.Value - history.StartTime.Value).TotalSeconds;
                }

                await _syncJobHistoryRepository.CreateAsync(history);
            }
        }
    }
}