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
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;

        public TeamsChannelUpdaterService(ILogger<TeamsChannelUpdaterService> logger,
            ITeamsChannelRepository teamsChannelRepository,
            IDatabaseSyncJobsRepository syncJobRepository, 
            IDatabaseGroupsRepository databaseGroupsRepository,
            IDatabaseChannelsRepository databaseChannelsRepository,
            IServiceBusQueueRepository serviceBusQueueRepository,
            ISyncJobStatusService syncJobStatusService,
            ISyncJobHistoryRepository syncJobHistoryRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
            _syncJobHistoryRepository = syncJobHistoryRepository ?? throw new ArgumentNullException(nameof(syncJobHistoryRepository));
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


        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId, int? usersAdded = null, int? usersRemoved = null)
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

            // TeamsChannelUpdater records end state, deltas, and AfterSyncUserCount when a run completes.
            var afterSyncUserCount = status == SyncStatus.Idle
                ? await CalculateAfterSyncUserCountAsync(runId, usersAdded, usersRemoved)
                : null;

            var historyPatch = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = runId,
                Status = status.ToString(),
                UpdatedByFunction = "TeamsChannelUpdater",
                UsersAdded = usersAdded,
                UsersRemoved = usersRemoved,
                EndTime = status != SyncStatus.InProgress ? currentDate : null,
                UpdatedAt = currentDate,
                AfterSyncUserCount = afterSyncUserCount
            };

            await _syncJobStatusService.UpdateJobStatusAsync(job, status, historyPatch, "TeamsChannelUpdater");

            var groupId = await GetGroupIdAsync(job);

            string message = isDryRunSync
                                ? $"Dry Run of a sync to {groupId} is complete. Membership will not be updated."
                                : $"Syncing to {groupId} done.";

            _logger.SyncStatusMessage(message);
        }

        // Calculate AfterSyncUserCount from the run's persisted BeforeSyncUserCount and membership delta.
        private async Task<int?> CalculateAfterSyncUserCountAsync(Guid runId, int? usersAdded, int? usersRemoved)
        {
            var existingHistory = await _syncJobHistoryRepository.GetByRunIdAsync(runId);

            if (existingHistory?.BeforeSyncUserCount.HasValue != true)
            {
                return null;
            }

            var usersAddedCount = usersAdded ?? 0;
            var usersRemovedCount = usersRemoved ?? 0;

            if (usersAddedCount > 0 || usersRemovedCount > 0)
            {
                return existingHistory.BeforeSyncUserCount.Value + usersAddedCount - usersRemovedCount;
            }

            return existingHistory.BeforeSyncUserCount.Value;
        }

        public async Task MarkSyncJobAsErroredAsync(SyncJob syncJob)
        {
            var now = DateTime.UtcNow;
            var historyPatch = new SyncJobHistory
            {
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId ?? Guid.Empty,
                Status = SyncStatus.Error.ToString(),
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

        // Render TeamsChannel destinations as "TeamName: ChannelName", falling back to ids as needed.
        public async Task<string> GetDestinationLabelAsync(SyncJob job, Guid runId)
        {
            var groupId = await GetGroupIdAsync(job);
            var teamName = await _teamsChannelRepository.GetGroupNameAsync(groupId, runId);
            if (string.IsNullOrWhiteSpace(teamName))
            {
                teamName = groupId.ToString();
            }

            if (job.MembershipType != MembershipTypes.TeamsChannelMembership.ToString())
            {
                return teamName;
            }

            var channelId = await GetChannelIdAsync(job);
            var channel = new AzureADTeamsChannel { ObjectId = groupId, ChannelId = channelId };
            string channelName = null;
            try
            {
                channelName = await _teamsChannelRepository.GetTeamsChannelNameAsync(channel);
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Failed to resolve channel name for group {groupId}, channel {channelId}: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(channelName))
            {
                channelName = channelId;
            }

            return $"{teamName}: {channelName}";
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
