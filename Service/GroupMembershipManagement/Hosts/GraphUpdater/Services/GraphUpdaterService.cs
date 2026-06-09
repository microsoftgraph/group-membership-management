// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.GraphUpdater;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Models.Notifications;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Metric = Services.Entities.Metric;
using System.Text.Json;

namespace Services
{
    public class GraphUpdaterService : IGraphUpdaterService
    {
        private const int NumberOfGraphRetries = 5;
        private const string EmailSubject = "EmailSubject";
        private readonly ILogger<GraphUpdaterService> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IMailRepository _mailRepository;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly INotificationTypesRepository _notificationTypesRepository;
		private readonly IJobNotificationsRepository _jobNotificationRepository;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;

        public GraphUpdaterService(
                ILogger<GraphUpdaterService> logger,
                TelemetryClient telemetryClient,
                IGraphGroupRepository graphGroupRepository,
                IMailRepository mailRepository,
                IEmailSenderRecipient emailSenderAndRecipients,
                IDatabaseSyncJobsRepository syncJobRepository,
                IDatabaseGroupsRepository databaseGroupsRepository,
                INotificationTypesRepository notificationTypesRepository,
			    IJobNotificationsRepository jobNotificationRepository,
                IServiceBusQueueRepository serviceBusQueueRepository,
            ISyncJobStatusService syncJobStatusService,
            ISyncJobHistoryRepository syncJobHistoryRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _jobNotificationRepository = jobNotificationRepository ?? throw new ArgumentNullException(nameof(jobNotificationRepository));
			_notificationTypesRepository = notificationTypesRepository ?? throw new ArgumentNullException(nameof(notificationTypesRepository));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(_serviceBusQueueRepository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
            _syncJobHistoryRepository = syncJobHistoryRepository ?? throw new ArgumentNullException(nameof(syncJobHistoryRepository));
        }

        public async Task<bool> GroupExistsAsync(Guid groupId)
        {
            return await _graphGroupRepository.GroupExists(groupId);
        }

        public async Task<Guid> GetGroupIdAsync(SyncJob syncJob)
        {
            if (syncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                var group = syncJob.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(syncJob.Id);
                return group.GroupId;
            }
            return Guid.Empty;
        }

        public async Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters)
        {
			 var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParameters }
            };
            var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
            var message = new ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());
            await _serviceBusQueueRepository.SendMessageAsync(message);
            _logger.SentNotificationQueueMessage(message.MessageId);
        }
        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId, int? usersAdded, int? usersRemoved)
        {
            _logger.JobStatusUpdated(status.ToString());

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

            // Calculate AfterSyncUserCount only when sync completes successfully
            var afterSyncUserCount = status == SyncStatus.Idle
                ? await CalculateAfterSyncUserCountAsync(runId, usersAdded, usersRemoved)
                : null;

            var history = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = runId,
                Status = status.ToString(),
                UpdatedByFunction = "GraphUpdater",
                ThresholdViolations = job.ThresholdViolations,
                UsersAdded = usersAdded,
                UsersRemoved = usersRemoved,
                EndTime = status != SyncStatus.InProgress ? currentDate : null,
                UpdatedAt = currentDate,
                AfterSyncUserCount = afterSyncUserCount
            };

            job.Status = status.ToString();

            await _syncJobStatusService.UpdateJobStatusAsync(job, status, history, functionName: "GraphUpdater");

            var groupId = await GetGroupIdAsync(job);

            if (isDryRunSync)
                _logger.DryRunComplete(groupId);
            else
                _logger.SyncComplete(groupId);
        }

        private async Task<int?> CalculateAfterSyncUserCountAsync(Guid runId, int? usersAdded, int? usersRemoved)
        {
            // Retrieve existing history to get BeforeSyncUserCount
            var existingHistory = await _syncJobHistoryRepository.GetByRunIdAsync(runId);

            if (existingHistory?.BeforeSyncUserCount.HasValue != true)
            {
                return null;
            }

            var usersAddedCount = usersAdded ?? 0;
            var usersRemovedCount = usersRemoved ?? 0;

            // Calculate only if there were actual changes
            if (usersAddedCount > 0 || usersRemovedCount > 0)
            {
                return existingHistory.BeforeSyncUserCount.Value + usersAddedCount - usersRemovedCount;
            }

            // No changes, count remains the same
            return existingHistory.BeforeSyncUserCount.Value;
        }



        public async Task<SyncJob> GetSyncJobAsync(Guid syncJobId)
        {
            return await _syncJobRepository.GetSyncJobAsync(syncJobId);
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound, List<AzureADUser> UsersAlreadyExist)> AddUsersToGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync)
        {
            var stopwatch = Stopwatch.StartNew();
            var graphResponse = await _graphGroupRepository.AddUsersToGroup(members, new AzureADGroup { ObjectId = targetGroupId });
            stopwatch.Stop();

            if (isInitialSync)
                _telemetryClient.TrackMetric(nameof(Metric.MembersAddedFromOnboarding), graphResponse.SuccessCount);
            else
                _telemetryClient.TrackMetric(nameof(Metric.MembersAdded), graphResponse.SuccessCount);

            _logger.UsersAddedToGroup(members.Count, targetGroupId, stopwatch.Elapsed.TotalSeconds, members.Count / stopwatch.Elapsed.TotalSeconds);
            _telemetryClient.TrackMetric(nameof(Metric.GraphAddRatePerSecond), members.Count / stopwatch.Elapsed.TotalSeconds);

            var status = graphResponse.ResponseCode == ResponseCode.GuestError ?
                GraphUpdaterStatus.GuestError :
                (graphResponse.ResponseCode == ResponseCode.Error ?
                    GraphUpdaterStatus.Error :
                    GraphUpdaterStatus.Ok);
            return (status, graphResponse.SuccessCount, graphResponse.UsersNotFound, graphResponse.UsersAlreadyExist);
        }

        public async Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound)> RemoveUsersFromGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync)
        {
            var stopwatch = Stopwatch.StartNew();
            var graphResponse = await _graphGroupRepository.RemoveUsersFromGroup(members, new AzureADGroup { ObjectId = targetGroupId });
            stopwatch.Stop();

            if (isInitialSync)
                _telemetryClient.TrackMetric(nameof(Metric.MembersRemovedFromOnboarding), graphResponse.SuccessCount);
            else
                _telemetryClient.TrackMetric(nameof(Metric.MembersRemoved), graphResponse.SuccessCount);

            _logger.UsersRemovedFromGroup(members.Count, targetGroupId, stopwatch.Elapsed.TotalSeconds, members.Count / stopwatch.Elapsed.TotalSeconds);
            _telemetryClient.TrackMetric(nameof(Metric.GraphRemoveRatePerSecond), members.Count / stopwatch.Elapsed.TotalSeconds);

            var status = graphResponse.ResponseCode == ResponseCode.Error ? GraphUpdaterStatus.Error : GraphUpdaterStatus.Ok;
            return (status, graphResponse.SuccessCount, graphResponse.UsersNotFound);
        }

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            return await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(email, groupObjectId);
        }

        public async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            return await _graphGroupRepository.GetGroupOwnersAsync(groupObjectId, top);
        }
    }
}
