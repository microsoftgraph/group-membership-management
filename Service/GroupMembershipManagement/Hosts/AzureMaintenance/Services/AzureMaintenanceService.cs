// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph.Models;
using Models;
using Models.AzureMaintenance;
using Models.Notifications;
using Models.ServiceBus;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services
{
    public class AzureMaintenanceService : IAzureMaintenanceService
	{
        private readonly IDatabaseSyncJobsRepository _syncJobRepository = null;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository = null;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository = null;
        private readonly IDatabasePurgedSyncJobsRepository _purgedSyncJobRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;
        private readonly IHandleInactiveJobsConfig _handleInactiveJobsConfig = null;
        private readonly INotificationRepository _notificationRepository = null;
        private readonly IServiceBusQueueRepository _notificationsQueueRepository;
        private readonly ILoggingRepository _loggingRepository;

        public AzureMaintenanceService(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseGroupsRepository databaseGroupsRepository,
            IDatabaseChannelsRepository databaseChannelsRepository,
            IDatabasePurgedSyncJobsRepository purgedSyncJobRepository,
            IGraphGroupRepository graphGroupRepository,
			IHandleInactiveJobsConfig handleInactiveJobsConfig,
            INotificationRepository notificationRepository,
            IServiceBusQueueRepository notificationQueueRepository,
            ILoggingRepository loggingRepository)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _purgedSyncJobRepository = purgedSyncJobRepository ?? throw new ArgumentNullException(nameof(purgedSyncJobRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _handleInactiveJobsConfig = handleInactiveJobsConfig ?? throw new ArgumentNullException(nameof(handleInactiveJobsConfig));
			_notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _notificationsQueueRepository = notificationQueueRepository ?? throw new ArgumentNullException(nameof(notificationQueueRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            var jobs = await _syncJobRepository.GetSyncJobsAsync(true,
                SyncStatus.CustomerPaused,
                SyncStatus.DestinationGroupNotFound,
                SyncStatus.MembershipDataNotFound,
                SyncStatus.NotOwnerOfDestinationGroup,
                SyncStatus.SecurityGroupNotFound,
                SyncStatus.ThresholdExceeded);

            var jobsToBePurged = ApplyPurgingFilters(jobs).ToList();

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Number of jobs to be purged: {jobsToBePurged.Count}"
            });

            return jobsToBePurged;
        }

        private IEnumerable<SyncJob> ApplyPurgingFilters(IEnumerable<SyncJob> jobs)
        {
            return jobs.Where(x => x.LastRunTime.AddDays(_handleInactiveJobsConfig.NumberOfDaysBeforePurging) <= DateTime.UtcNow);
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task SendPurgingEmailAsync(PurgedSyncJob job, NotificationMessageType notificationType)
        {
            var groupName = await GetGroupNameAsync(job.TargetOfficeGroupId);
            var additionalContentParams = new[]
            {
                job.TargetOfficeGroupId.ToString(),
                groupName,
                DateTime.UtcNow.AddDays(_handleInactiveJobsConfig.NumberOfDaysBeforeDeletion).ToString()
            };

            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParams }
            };

            await SendEmailAsync(messageContent, job.Id, job.RunId, notificationType);
        }

        public async Task SendWarningEmailAsync(SyncJob job, NotificationMessageType notificationType)
        {
            var groupName = await GetGroupNameAsync(job.TargetOfficeGroupId);
            string[] additionalContentParams;

            // Parameters for warning email: status, since date, allowed days, purge date
            var purgeDate = job.LastRunTime.AddDays(_handleInactiveJobsConfig.NumberOfDaysBeforePurging);
            additionalContentParams = new[]
            {
                job.Status,
                job.LastRunTime.ToString("MMMM dd, yyyy"),
                _handleInactiveJobsConfig.NumberOfDaysBeforePurging.ToString(),
                purgeDate.ToString("MMMM dd, yyyy"),
                job.TargetOfficeGroupId.ToString(),
                groupName
            };
            
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParams }
            };

            await SendEmailAsync(messageContent, job.Id, job.RunId, notificationType);
        }

        private async Task SendEmailAsync(Dictionary<string, Object> messageContent, Guid jobId, Guid? runId, NotificationMessageType notificationType)
        {
            var body = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(messageContent));
            var message = new ServiceBusMessage
            {
                MessageId = $"{jobId}_{runId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());
            await _notificationsQueueRepository.SendMessageAsync(message);
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = runId,
                Message = $"Sent message {message.MessageId} to service bus notifications queue "
            });
        }
        private async Task<Guid> GetGroupIdAsync(SyncJob syncJob)
        {
            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                var destination = syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
                return destination?.GroupId ?? Guid.Empty;

            }
            else if (syncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                var destination = syncJob.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(syncJob.Id);
                return destination?.GroupId ?? Guid.Empty;
            }
            return Guid.Empty;
        }

        public async Task<List<PurgedSyncJob>> BackupInactiveJobsAsync(List<SyncJob> syncJobs)
        {
            if (syncJobs.Count <= 0) return new List<PurgedSyncJob>();

            var purgedJobs = await MapSyncJobsToPurgedSyncJobsAsync(syncJobs);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Number of purged jobs: {purgedJobs.Count}"
            });

            await _purgedSyncJobRepository.InsertPurgedSyncJobsAsync(purgedJobs);

            return purgedJobs;
        }

        private async Task<List<PurgedSyncJob>> MapSyncJobsToPurgedSyncJobsAsync(List<SyncJob> syncJobs)
        {
            var purgedSyncJobs = new List<PurgedSyncJob>();

            foreach (var job in syncJobs)
            {
                var purgedJob = await MapSyncJobToPurgedSyncJobAsync(job);
                var groupName = await GetGroupNameAsync(purgedJob.TargetOfficeGroupId);
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Purging Job with GroupId: {purgedJob.TargetOfficeGroupId}, GroupName: {groupName} and Status: {purgedJob.Status}"
                });
                purgedSyncJobs.Add(purgedJob);
            }

            return purgedSyncJobs;
        }

        private async Task<PurgedSyncJob> MapSyncJobToPurgedSyncJobAsync(SyncJob job)
        {
            var groupId = await GetGroupIdAsync(job);
            var destination = JsonSerializer.Serialize(new[]
            {
                new { type = job.MembershipType.ToString(), value = new { objectId = groupId } }
            });

            return new PurgedSyncJob
            {
                Id = Guid.NewGuid(),
                IgnoreThresholdOnce = job.IgnoreThresholdOnce,
                IsDryRunEnabled = job.IsDryRunEnabled,
                DryRunTimeStamp = job.DryRunTimeStamp,
                LastRunTime = job.LastRunTime,
                LastSuccessfulRunTime = job.LastSuccessfulRunTime,
                LastSuccessfulStartTime = job.LastSuccessfulStartTime,
                StartDate = job.StartDate,
                TargetOfficeGroupId = groupId,
                Destination = destination,
                AllowEmptyDestination = job.AllowEmptyDestination,
                RunId = job.RunId,
                Period = job.Period,
                ThresholdPercentageForAdditions = job.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = job.ThresholdPercentageForRemovals,
                ThresholdViolations = job.ThresholdViolations,
                Query = job.Query,
                Requestor = job.Requestor,
                Status = job.Status,
                PurgedAt = DateTime.UtcNow
            };
        }

        public async Task<int> RemoveBackupsAsync()
        {
            var cutOffDate = DateTime.UtcNow.AddDays(-_handleInactiveJobsConfig.NumberOfDaysBeforeDeletion);
            var jobs = await _purgedSyncJobRepository.GetPurgedSyncJobsAsync(cutOffDate);
            if (jobs.ToList().Count <= 0) return 0;
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Number of jobs to be deleted from PurgedSyncJobs table: {jobs.Count()}"
            });
            return await _purgedSyncJobRepository.DeletePurgedSyncJobsAsync(jobs);
        }

        public async Task RemoveInactiveJobsAsync(IEnumerable<SyncJob> jobs)
        {
            await _syncJobRepository.DeleteSyncJobsAsync(jobs);
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Number of jobs deleted from SyncJobs table: {jobs.Count()}"
            });
        }

		public async Task ExpireNotificationsAsync(IEnumerable<SyncJob> jobs)
		{
            foreach (var job in jobs)
			{
                var thresholdNotification = await _notificationRepository.GetThresholdNotificationBySyncJobIdAsync(job.Id);
				if (thresholdNotification != null)
				{
					thresholdNotification.Status = ThresholdNotificationStatus.Expired;
					thresholdNotification.CardState = ThresholdNotificationCardState.ExpiredCard;
					await _notificationRepository.SaveNotificationAsync(thresholdNotification);
				}
            }
		}

        public async Task<List<SyncJob>> GetJobsApproachingPurgingAsync()
        {
            var jobsEligibleForPurging = await _syncJobRepository.GetSyncJobsAsync(false,
                SyncStatus.CustomerPaused,
                SyncStatus.DestinationGroupNotFound,
                SyncStatus.MembershipDataNotFound,
                SyncStatus.NotOwnerOfDestinationGroup,
                SyncStatus.SecurityGroupNotFound,
                SyncStatus.ThresholdExceeded);
            
            var warningCutOffDate = DateTime.UtcNow.Date.AddDays(_handleInactiveJobsConfig.NumberOfDaysBeforePurgingToSendWarning - _handleInactiveJobsConfig.NumberOfDaysBeforePurging);

            var jobsNeedingWarning = jobsEligibleForPurging
                .Where(job => job.LastRunTime.Date == warningCutOffDate)
                .ToList();

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Jobs needing warning as of {warningCutOffDate}: {jobsNeedingWarning.Count}"
            });
            
            return jobsNeedingWarning;
        }
    }
}