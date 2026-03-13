// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Models;
using Models.Entities;
using Models.Helpers;
using Models.Notifications;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Services
{
    public class JobTriggerService : IJobTriggerService
    {

        enum Metric
        {
            PotentialSyncJobCount,
            TotalSyncJobsCount,
            ActiveInProgressJobCount,
            JobsDueToRunCount,
            JobsToBeStarted
        }

        private readonly ILogger<JobTriggerService> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly IDatabaseDestinationAttributesRepository _databaseDestinationAttributesRepository;
        private readonly INotificationTypesRepository _notificationTypesRepository;
        private readonly IJobNotificationsRepository _jobNotificationRepository;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly string _gmmAppId;
        private readonly Guid _gmmTeamsChannelServiceAccountId;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly IGMMResources _gmmResources;
        private readonly IJobTriggerConfig _jobTriggerConfig;
        private readonly TelemetryClient _telemetryClient;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public JobTriggerService(
            ILogger<JobTriggerService> logger,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            IDatabaseGroupsRepository databaseGroupsRepository,
            IDatabaseChannelsRepository databaseChannelsRepository,
            IDatabaseDestinationAttributesRepository databaseDestinationAttributesRepository,
            INotificationTypesRepository notificationTypesRepository,
            IJobNotificationsRepository jobNotificationRepository,
            IServiceBusTopicsRepository serviceBusTopicsRepository,
            IGraphGroupRepository graphGroupRepository,
            ITeamsChannelRepository teamsChannelRepository,
            IKeyVaultSecret<IJobTriggerService> gmmAppId,
            IKeyVaultSecret<IJobTriggerService, Guid> gmmTeamsChannelServiceAccountId,
            IEmailSenderRecipient emailSenderAndRecipients,
            IServiceBusQueueRepository serviceBusQueueRepository,
            IGMMResources gmmResources,
            IJobTriggerConfig jobTriggerConfig,
            TelemetryClient telemetryClient,
            ISyncJobStatusService syncJobStatusService
            )
        {
            _emailSenderAndRecipients = emailSenderAndRecipients;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _databaseDestinationAttributesRepository = databaseDestinationAttributesRepository ?? throw new ArgumentNullException(nameof(databaseDestinationAttributesRepository));
            _jobNotificationRepository = jobNotificationRepository ?? throw new ArgumentNullException(nameof(jobNotificationRepository));
            _notificationTypesRepository = notificationTypesRepository ?? throw new ArgumentNullException(nameof(notificationTypesRepository));
            _serviceBusTopicsRepository = serviceBusTopicsRepository ?? throw new ArgumentNullException(nameof(serviceBusTopicsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException( nameof(teamsChannelRepository));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(_serviceBusQueueRepository));
            _gmmAppId = gmmAppId.Secret;
            _gmmTeamsChannelServiceAccountId = gmmTeamsChannelServiceAccountId.Secret;
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _jobTriggerConfig = jobTriggerConfig ?? throw new ArgumentNullException(nameof(jobTriggerConfig));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
        }

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsAsync(false, SyncStatus.Idle, SyncStatus.InProgress, SyncStatus.StuckInProgress, SyncStatus.TransientError);
            var jobsDueToRun = ApplyJobTriggerFilters(jobs).ToList();
            var jobsExcludingFiltered = jobs.Except(jobsDueToRun).ToList();
            var activeInProgressJobs = jobsExcludingFiltered.Where(job => job.Status == SyncStatus.InProgress.ToString()).ToList();
            var potentialSyncJobCount = jobsDueToRun.Count + activeInProgressJobs.Count;
            var totalSyncJobsCount = await _databaseSyncJobsRepository.GetSyncJobCountAsync(SyncStatus.All);

            _telemetryClient.TrackMetric(nameof(Metric.ActiveInProgressJobCount), activeInProgressJobs.Count);
            _telemetryClient.TrackMetric(nameof(Metric.JobsDueToRunCount), jobsDueToRun.Count);
            _telemetryClient.TrackMetric(nameof(Metric.PotentialSyncJobCount), potentialSyncJobCount);
            _telemetryClient.TrackMetric(nameof(Metric.TotalSyncJobsCount), totalSyncJobsCount);

            var jobTriggerThresholdExceeded = HasJobTriggerThresholdExceeded(potentialSyncJobCount, totalSyncJobsCount);

            var jobsToBeStarted = jobsDueToRun;
            if (jobTriggerThresholdExceeded)
            {
                jobsToBeStarted = jobsToBeStarted.OrderBy(job => job.ScheduledDate).Take(_jobTriggerConfig.JobCountThreshold).ToList();
            }
            _telemetryClient.TrackMetric(nameof(Metric.JobsToBeStarted), jobsToBeStarted.Count);

            return jobsToBeStarted;
        }

        public async Task<SyncJob> GetSyncJobByIdAsync(Guid syncJobId)
        {
            return await _databaseSyncJobsRepository.GetSyncJobAsync(syncJobId);
        }

        public async Task<string> GetDestinationNameAsync(SyncJob job)
        {
            // Try to get the name from the DestinationNames table first

            var destinationName = await _databaseDestinationAttributesRepository.GetDestinationName(job);
            if(destinationName != null)
            {
                return destinationName;
            }

            if (job.MembershipType == "TeamsChannelMembership")
            {
                var channel = new AzureADTeamsChannel
                {
                    ObjectId = job.Channel.GroupId,
                    ChannelId = job.Channel.ChannelId
                };

                return await _teamsChannelRepository.GetTeamsChannelNameAsync(channel);
            }
            else if (job.MembershipType == "GroupMembership")
            {
                var objectId = job.Group.GroupId;
                return await _graphGroupRepository.GetGroupNameAsync(objectId);
            }

            return "";
        }

        public async Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters)
        {
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParameters }
            };
            var body = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(messageContent));
            var message = new ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());
            await _serviceBusQueueRepository.SendMessageAsync(message);
            using (_logger.BeginSyncJobScope(job))
            {
                _logger.SentNotificationMessage(message.MessageId);
            }

        }

        public async Task UpdateSyncJobAsync(SyncStatus? status, SyncJob job)
        {
            var now = DateTime.UtcNow;
            SyncJobHistory history = null;

            if (status == SyncStatus.InProgress)
            {
                using (_logger.BeginSyncJobScope(job))
                {
                    _logger.StartingJob();
                }

                job.LastSuccessfulStartTime = now;
            }

            if (status == SyncStatus.StuckInProgress)
            {
                using (_logger.BeginSyncJobScope(job))
                {
                    _logger.RestartingStuckJob();
                }

                job.LastRunTime = now;
                job.LastSuccessfulStartTime = now;
            }

            if (status.HasValue
                && status.Value != SyncStatus.InProgress
                && status.Value != SyncStatus.StuckInProgress
                && job.RunId.HasValue)
            {
                history = new SyncJobHistory
                {
                    SyncJobId = job.Id,
                    RunId = job.RunId.Value,
                    EndTime = now,
                    Status = status.Value.ToString(),
                    UpdatedByFunction = "JobTrigger"
                };
            }

            await _syncJobStatusService.UpdateJobStatusAsync(job, status, history, functionName: "JobTrigger");
        }
        public async Task SendMessageAsync(SyncJob job)
        {
            await _serviceBusTopicsRepository.AddMessageAsync(job);
        }
        public async Task<DestinationVerifierResult> DestinationExistsAndGMMCanWriteToItAsync(SyncJob job)
        {
            if (job.MembershipType == "TeamsChannelMembership")
                return await TeamsChannelExistsAndGMMCanWriteToItAsync(job);
            else if (job.MembershipType == "GroupMembership")
                return await GroupExistsAndGMMCanWriteToItAsync(job);
            else
                return DestinationVerifierResult.NotFound;
        }
        public async Task<List<string>> GetGroupEndpointsAsync(SyncJob job)
        {
            var destinationObjectId = job.MembershipType switch
            {
                var type when type == MembershipTypes.TeamsChannelMembership.ToString() => job.Channel.GroupId,
                var type when type == MembershipTypes.GroupMembership.ToString() => job.Group.GroupId,
                _ => Guid.Empty
            };

            return await _graphGroupRepository.GetGroupEndpointsAsync(destinationObjectId);
        }

        public async Task<Group> GetGroupAsync(SyncJob syncJob)
        {
            return syncJob.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(syncJob.Id);
        }
        public async Task<Channel> GetChannelAsync(SyncJob syncJob)
        {
            return syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
        }
        public async Task<ParsedAndValidateDestinationResponse> ParseAndValidateDestinationAsync(SyncJob syncJob)
        {
            var destinationObject = DestinationParser.ParseDestination(syncJob);

            if (destinationObject == null)
            {
                return new ParsedAndValidateDestinationResponse{
                    IsValid = false,
                    DestinationObject = null
                };
            }
            else
            {
                var options = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
                var serializedDestinationObject = JsonSerializer.Serialize(destinationObject, options);

                return new ParsedAndValidateDestinationResponse
                {
                    IsValid = true,
                    DestinationObject = serializedDestinationObject
                };
            }
        }
        private IEnumerable<SyncJob> ApplyJobTriggerFilters(IEnumerable<SyncJob> jobs)
        {
            var allDueToRunJobs = jobs.Where(x => !x.IsDryRunEnabled && x.Status != SyncStatus.InProgress.ToString());
            var inProgressSyncJobs = jobs.Where(x => ((DateTime.UtcNow - x.LastSuccessfulStartTime) > TimeSpan.FromHours(x.Period)) && x.Status == SyncStatus.InProgress.ToString());
            return allDueToRunJobs.Concat(inProgressSyncJobs);
        }
        private bool HasJobTriggerThresholdExceeded(int syncJobsCount, int totalSyncJobsCount)
        {
            if (syncJobsCount < _jobTriggerConfig.JobCountThreshold)
            {
                return false;
            }
            else if (syncJobsCount >= _jobTriggerConfig.JobCountThreshold)
            {
                double PerMille = ((double)syncJobsCount / totalSyncJobsCount) * 1000;

                if (PerMille >= _jobTriggerConfig.JobPerMilleThreshold)
                {
                    return true;
                }
            }
            return false;
        }
        private async Task<DestinationVerifierResult> GroupExistsAndGMMCanWriteToItAsync(SyncJob job)
        {
            var groupId = job.Group.GroupId;

            if (!(await CheckGroupExists(job, groupId)))
                return DestinationVerifierResult.NotFound;
            if (!_jobTriggerConfig.GMMHasGroupReadWriteAllPermissions && !(await CheckGMMIsGroupOwner(job, groupId)))
                return DestinationVerifierResult.NotOwnedByGMM;
            return DestinationVerifierResult.Success;
        }
        private async Task<DestinationVerifierResult> TeamsChannelExistsAndGMMCanWriteToItAsync(SyncJob job)
        {
            var destinationObject = job.Channel;
            var channel = new AzureADTeamsChannel
            {
                ObjectId = destinationObject.GroupId,
                ChannelId = destinationObject.ChannelId
            };

            if (!await CheckTeamExists(job, channel) || !await CheckChannelExists(job, channel))
                return DestinationVerifierResult.NotFound;

            if(!_jobTriggerConfig.GMMHasChannelReadWriteAllPermissions && !await CheckGMMIsChannelOwner(job, channel))
                return DestinationVerifierResult.NotOwnedByGMM;

            return DestinationVerifierResult.Success;
        }
        private async Task<bool> CheckGroupExists(SyncJob job, Guid groupId)
        {
            return await CheckAndLogAsync(job, $"group {groupId}",
                () => _graphGroupRepository.GroupExists(groupId));
        }
        private async Task<bool> CheckGMMIsGroupOwner(SyncJob job, Guid groupId)
        {
            return await CheckAndLogAsync(job, $"GMM ownership of group {groupId}",
                () => _graphGroupRepository.IsAppIDOwnerOfGroup(_gmmAppId, groupId));
        }
        private async Task<bool> CheckChannelExists(SyncJob job, AzureADTeamsChannel channel)
        {
            return await CheckAndLogAsync(job, $"channel {channel.ChannelId} in team {channel.ObjectId}",
                () => _teamsChannelRepository.TeamsChannelExistsAsync(channel, job.RunId));
        }
        private async Task<bool> CheckTeamExists(SyncJob job, AzureADTeamsChannel channel)
        {
            return await CheckAndLogAsync(job, $"team {channel.ObjectId}",
                () => _graphGroupRepository.GroupExists(channel.ObjectId));
        }
        private async Task<bool> CheckGMMIsChannelOwner(SyncJob job, AzureADTeamsChannel channel)
        {
            return await CheckAndLogAsync(job, $"GMM ownership of channel {channel.ChannelId} in team {channel.ObjectId}",
                () => _teamsChannelRepository.IsServiceAccountOwnerOfChannelAsync(_gmmTeamsChannelServiceAccountId, channel, job.RunId));
        }
        private async Task<bool> CheckAndLogAsync(SyncJob job, string checkDescription, Func<Task<bool>> checkFunc)
        {
            using (_logger.BeginSyncJobScope(job))
            {
                _logger.CheckingExists(checkDescription);
                bool result = await checkFunc();
                _logger.CheckResult(result ? "passed" : "failed", checkDescription, result ? "exists" : "does not exist");
                return result;
            }
        }

    }
}
