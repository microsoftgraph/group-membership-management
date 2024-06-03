// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
using System.Threading.Tasks;

namespace Services
{
    public class AzureMaintenanceService : IAzureMaintenanceService
	{
        private readonly IDatabaseSyncJobsRepository _syncJobRepository = null;
        private readonly IDatabasePurgedSyncJobsRepository _purgedSyncJobRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;
        private readonly IHandleInactiveJobsConfig _handleInactiveJobsConfig = null;
        private readonly INotificationRepository _notificationRepository = null;
        private readonly IServiceBusQueueRepository _notificationsQueueRepository;
        private readonly ILoggingRepository _loggingRepository;

        public AzureMaintenanceService(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabasePurgedSyncJobsRepository purgedSyncJobRepository,
            IGraphGroupRepository graphGroupRepository,
			IHandleInactiveJobsConfig handleInactiveJobsConfig,
            INotificationRepository notificationRepository,
            IServiceBusQueueRepository notificationQueueRepository,
            ILoggingRepository loggingRepository)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _purgedSyncJobRepository = purgedSyncJobRepository ?? throw new ArgumentNullException(nameof(purgedSyncJobRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _handleInactiveJobsConfig = handleInactiveJobsConfig ?? throw new ArgumentNullException(nameof(handleInactiveJobsConfig));
			_notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _notificationsQueueRepository = notificationQueueRepository ?? throw new ArgumentNullException(nameof(notificationQueueRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            var jobs = await _syncJobRepository.GetSyncJobsAsync(false,
                SyncStatus.CustomerPaused,
                SyncStatus.DestinationGroupNotFound,
                SyncStatus.MembershipDataNotFound,
                SyncStatus.NotOwnerOfDestinationGroup,
                SyncStatus.SecurityGroupNotFound,
                SyncStatus.ThresholdExceeded,
                SyncStatus.SubmissionRejected);

            return ApplyJobTriggerFilters(jobs).ToList();
        }

        private IEnumerable<SyncJob> ApplyJobTriggerFilters(IEnumerable<SyncJob> jobs)
        {
            return jobs.Where(x => ((DateTime.UtcNow - x.LastRunTime) > TimeSpan.FromDays(30)));
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParams)
        {
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParams }
            };
            var body = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(messageContent));
            var message = new ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());
            await _notificationsQueueRepository.SendMessageAsync(message);
            await _loggingRepository.LogMessageAsync(new LogMessage 
            {            
                RunId = job.RunId,
                Message = $"Sent message {message.MessageId} to service bus notifications queue "
            });


        }

        public async Task<int> BackupInactiveJobsAsync(List<SyncJob> syncJobs)
        {
            if (syncJobs.Count <= 0) return 0;
            var purgedJobs = MapSyncJobsToPurgedSyncJobs(syncJobs);
            return await _purgedSyncJobRepository.InsertPurgedSyncJobsAsync(purgedJobs);
        }

        private List<PurgedSyncJob> MapSyncJobsToPurgedSyncJobs(List<SyncJob> syncJobs)
        {
            return syncJobs.Select(x => MapSyncJobToPurgedSyncJob(x)).ToList();
        }

        private PurgedSyncJob MapSyncJobToPurgedSyncJob(SyncJob job)
        {
            return new PurgedSyncJob()
            {
                Id = new Guid(),
                IgnoreThresholdOnce = job.IgnoreThresholdOnce,
                IsDryRunEnabled = job.IsDryRunEnabled,
                DryRunTimeStamp = job.DryRunTimeStamp,
                LastRunTime = job.LastRunTime,
                LastSuccessfulRunTime = job.LastSuccessfulRunTime,
                LastSuccessfulStartTime = job.LastSuccessfulStartTime,
                StartDate = job.StartDate,
                TargetOfficeGroupId = job.TargetOfficeGroupId,
                Destination = job.Destination,
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
            return await _purgedSyncJobRepository.DeletePurgedSyncJobsAsync(jobs);
        }

        public async Task RemoveInactiveJobsAsync(IEnumerable<SyncJob> jobs)
        {
            await _syncJobRepository.DeleteSyncJobsAsync(jobs);
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
    }
}