// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.SyncJobUpdater
{
    public class SyncJobUpdaterService : ISyncJobUpdaterService
    {
        private readonly ILogger<SyncJobUpdaterService> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public SyncJobUpdaterService(
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ILogger<SyncJobUpdaterService> logger,
            ISyncJobStatusService syncJobStatusService)
        {
            _logger = logger;
            _databaseSyncJobsRepository = databaseSyncJobsRepository;
            _syncJobStatusService = syncJobStatusService;
        }

        public async Task UpdateSyncJobStatusAsync(JobStatusUpdateQueueMessage message)
        {
            // Get the sync job from the message or from the database
            SyncJob syncJob = message.SyncJob ?? await _databaseSyncJobsRepository.GetSyncJobAsync(message.JobId);
            
            if (syncJob == null)
            {
                _logger.SyncJobNotFound(message.JobId);
                return;
            }

            // Update threshold violations if provided
            if (message.ThresholdViolations.HasValue)
            {
                syncJob.ThresholdViolations = message.ThresholdViolations.Value;
            }

            var isDryRunSync = syncJob.IsDryRunEnabled;
            var currentDate = DateTime.UtcNow;
            if (isDryRunSync)
            {
                syncJob.DryRunTimeStamp = currentDate;
            }
            else
            {
                if (message.NewStatus == SyncStatus.Idle)
                {
                    syncJob.LastSuccessfulRunTime = currentDate;
                    syncJob.IgnoreThresholdOnce = false;
                }

                syncJob.LastRunTime = currentDate;
            }

            syncJob.ScheduledDate = currentDate.AddHours(syncJob.Period);

            // Update the sync job status
            var history = new SyncJobHistory
            {
                SyncJobId = message.JobId,
                RunId = message.RunId,
                Status = message.NewStatus.ToString(),
                UpdatedByFunction = message.UpdatedByFunction,
                StartTime = message.JobStartTime,
                EndTime = message.JobEndTime,
                UsersAdded = message.UsersAddedCount,
                UsersRemoved = message.UsersRemovedCount,
                ThresholdViolations = message.ThresholdViolations,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _syncJobStatusService.UpdateJobStatusAsync(syncJob, message.NewStatus, history);

            _logger.SyncJobStatusUpdated(message.JobId, message.NewStatus.ToString());
        }

    }
}