// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging;
using Models;
using Models.Entities;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Services.Contracts;
using Services.TeamsChannelUpdater.Contracts;
using Hosts.TeamsChannelUpdater;
using Models.Notifications;
using Models.ServiceBus;
using System.Text.Json;
using System.Text;

namespace Services.TeamsChannelUpdater
{
    public class TeamsChannelUpdaterService : ITeamsChannelUpdaterService
    {
        private const int NumberOfGraphRetries = 5;
        private const string EmailSubject = "EmailSubject";

        private readonly ILogger<TeamsChannelUpdaterService> _logger;
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public TeamsChannelUpdaterService(ILogger<TeamsChannelUpdaterService> logger,
            ITeamsChannelRepository teamsChannelRepository,
            IDatabaseSyncJobsRepository syncJobRepository, 
            IDatabaseGroupsRepository databaseGroupsRepository,
            IDatabaseChannelsRepository databaseChannelsRepository,
            IServiceBusQueueRepository serviceBusQueueRepository,
            ISyncJobStatusService syncJobStatusService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
        }

        public async Task<Guid> GetGroupIdAsync(SyncJob syncJob)
        {
            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                var channel = syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
                return channel.GroupId;

            }
            else if (syncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                var group = syncJob.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(syncJob.Id);
                return group.GroupId;
            }
            return Guid.Empty;
        }

        public async Task<string> GetChannelIdAsync(SyncJob syncJob)
        {
            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                var channel = syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
                return channel.ChannelId;
            }
            return string.Empty;
        }

        public async Task<SyncJob> GetSyncJobAsync(Guid syncJobId)
        {
            return await _syncJobRepository.GetSyncJobAsync(syncJobId);
        }


        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId)
        {
            _logger.SettingJobStatus(status.ToString());

            var isDryRunSync = job.IsDryRunEnabled || isDryRun;

            var currentDate = DateTime.UtcNow;
            if (isDryRunSync)
            {
                job.DryRunTimeStamp = currentDate;
            }
            else
            {
                if (status == SyncStatus.Idle)
                    job.LastSuccessfulRunTime = currentDate;

                job.LastRunTime = currentDate;
            }

            job.ScheduledDate = currentDate.AddHours(job.Period);
            job.RunId = runId;

            // TeamsChannelUpdater only owns end-time + threshold violations history updates.
            // StartTime is created by JobTrigger when the run is created.
            var historyPatch = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = runId,
                Status = status.ToString(),
                ThresholdViolations = job.ThresholdViolations,
                UpdatedByFunction = "TeamsChannelUpdater",
                EndTime = status != SyncStatus.InProgress ? currentDate : null,
                UpdatedAt = currentDate
            };

            await _syncJobStatusService.UpdateJobStatusAsync(job, status, historyPatch, "TeamsChannelUpdater");

            var groupId = await GetGroupIdAsync(job);

            string message = isDryRunSync
                                ? $"Dry Run of a sync to {groupId} is complete. Membership will not be updated."
                                : $"Syncing to {groupId} done.";

            _logger.SyncStatusMessage(message);
        }

        public async Task MarkSyncJobAsErroredAsync(SyncJob syncJob)
        {
            var now = DateTime.UtcNow;
            var historyPatch = new SyncJobHistory
            {
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId ?? Guid.Empty,
                Status = SyncStatus.Error.ToString(),
                ThresholdViolations = syncJob.ThresholdViolations,
                UpdatedByFunction = "TeamsChannelUpdater",
                EndTime = now,
                UpdatedAt = now
            };

            await _syncJobStatusService.UpdateJobStatusAsync(syncJob, SyncStatus.Error, historyPatch, "TeamsChannelUpdater");
        }

        public async Task<(int SuccessCount, List<AzureADTeamsUser> UsersToRetry, List<AzureADTeamsUser> UsersNotFound)> AddUsersToChannelAsync(AzureADTeamsChannel azureADTeamsChannel, List<AzureADTeamsUser> members)
        {
            var response = await _teamsChannelRepository.AddUsersToChannelAsync(azureADTeamsChannel, members);

            return response;
        }

        public async Task<(int SuccessCount, List<AzureADTeamsUser> UserRemovesFailed)> RemoveUsersFromChannelAsync(AzureADTeamsChannel azureADTeamsChannel, List<AzureADTeamsUser> members)
        {
            var response = await _teamsChannelRepository.RemoveUsersFromChannelAsync(azureADTeamsChannel, members);

            return response;
        }

        public async Task<string> GetGroupNameAsync(Guid groupId, Guid runId)
        {
            return await _teamsChannelRepository.GetGroupNameAsync(groupId, runId);
        }

        public async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, Guid runId, int top = 0)
        {
            return await _teamsChannelRepository.GetGroupOwnersAsync(groupObjectId, runId, top);
        }

        public async Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParams)
        {
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParams }
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
            var message = new ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());

            await _serviceBusQueueRepository.SendMessageAsync(message);
            _logger.SentNotificationMessage(message.MessageId);
        }

    }
}
