// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Services.Contracts;
using System.Threading.Tasks;

namespace Hosts.SyncJobUpdater
{
    public class SyncJobUpdaterService : ISyncJobUpdaterService
    {
        private readonly ILoggingRepository _log;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;

        public SyncJobUpdaterService(
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ILoggingRepository logging,
            ISyncJobHistoryRepository syncJobHistoryRepository)
        {
            _log = logging;
            _databaseSyncJobsRepository = databaseSyncJobsRepository;
            _syncJobHistoryRepository = syncJobHistoryRepository;
        }

        public async Task UpdateSyncJobStatusAsync(JobStatusUpdateQueueMessage message)
        {
            // Get the sync job from the message or from the database
            SyncJob syncJob = message.SyncJob ?? await _databaseSyncJobsRepository.GetSyncJobAsync(message.JobId);
            
            if (syncJob == null)
            {
                await _log.LogMessageAsync(new LogMessage 
                { 
                    Message = $"Unable to find sync job with ID {message.JobId}",
                    RunId = message.RunId 
                });
                return;
            }

            // Update threshold violations if provided
            if (message.ThresholdViolations.HasValue)
            {
                syncJob.ThresholdViolations = message.ThresholdViolations.Value;
            }

            // Update the sync job status
            await _databaseSyncJobsRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, message.NewStatus);

            // Create or update job history
            await CreateOrUpdateJobHistoryAsync(message);

            await _log.LogMessageAsync(new LogMessage 
            { 
                Message = $"Updated sync job {message.JobId} status to {message.NewStatus}",
                RunId = message.RunId 
            });
        }

        private async Task CreateOrUpdateJobHistoryAsync(JobStatusUpdateQueueMessage message)
        {
            // Check if a history entry already exists for this run
            var existingHistory = await _syncJobHistoryRepository.GetByRunIdAsync(message.RunId);
            
            if (existingHistory != null)
            {
                // Update existing history entry - preserve existing values if new values are null/empty
                existingHistory.EndTime = message.JobEndTime ?? existingHistory.EndTime;
                existingHistory.Status = message.NewStatus.ToString();
                existingHistory.UsersAdded = message.UsersAddedCount ?? existingHistory.UsersAdded;
                existingHistory.UsersRemoved = message.UsersRemovedCount ?? existingHistory.UsersRemoved;
                existingHistory.ThresholdViolations = message.ThresholdViolations ?? existingHistory.ThresholdViolations;
                existingHistory.UpdatedByFunction = !string.IsNullOrEmpty(message.UpdatedByFunction) ? message.UpdatedByFunction : existingHistory.UpdatedByFunction;
                
                // Update StartTime only if provided and not already set
                if (message.JobStartTime.HasValue && !existingHistory.StartTime.HasValue)
                {
                    existingHistory.StartTime = message.JobStartTime;
                }
                
                // Calculate duration if both start and end times are available
                if (existingHistory.StartTime.HasValue && existingHistory.EndTime.HasValue)
                {
                    existingHistory.Duration = (int)(existingHistory.EndTime.Value - existingHistory.StartTime.Value).TotalSeconds;
                }
                
                await _syncJobHistoryRepository.UpdateAsync(existingHistory);
            }
            else
            {
                // Create new history entry
                var newHistory = new SyncJobHistory
                {
                    SyncJobId = message.JobId,
                    RunId = message.RunId,
                    StartTime = message.JobStartTime,
                    EndTime = message.JobEndTime,
                    Status = message.NewStatus.ToString(),
                    UsersAdded = message.UsersAddedCount,
                    UsersRemoved = message.UsersRemovedCount,
                    ThresholdViolations = message.ThresholdViolations,
                    UpdatedByFunction = message.UpdatedByFunction
                };

                // Calculate duration if both start and end times are available
                if (newHistory.StartTime.HasValue && newHistory.EndTime.HasValue)
                {
                    newHistory.Duration = (int)(newHistory.EndTime.Value - newHistory.StartTime.Value).TotalSeconds;
                }

                await _syncJobHistoryRepository.CreateAsync(newHistory);
            }
        }
    }
}