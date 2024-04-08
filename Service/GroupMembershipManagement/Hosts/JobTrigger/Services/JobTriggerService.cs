// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Models;
using Models.Entities;
using Models.Notifications;
using Models.ServiceBus;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Models.Helpers;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Services
{
    public class JobTriggerService : IJobTriggerService
    {

        enum Metric
        {
            SyncJobsCount,
            TotalSyncJobsCount
        }

        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
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

        private Guid _runId;
        public Guid RunId
        {
            get { return _runId; }
            set
            {
                _runId = value;
                _graphGroupRepository.RunId = value;
            }
        }

        public JobTriggerService(
            ILoggingRepository loggingRepository,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
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
            TelemetryClient telemetryClient
            )
        {
            _emailSenderAndRecipients = emailSenderAndRecipients;
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
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
        }

        public async Task<(List<SyncJob> jobs, bool jobTriggerThresholdExceeded, int maxJobsAllowed)> GetSyncJobsAsync()
        {
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsAsync(SyncStatus.Idle, SyncStatus.InProgress, SyncStatus.StuckInProgress, SyncStatus.TransientError);
            var filteredJobs = ApplyJobTriggerFilters(jobs).ToList();
            var jobsExcludingFiltered = jobs.Except(filteredJobs).ToList();
            var activeInProgressJobs = jobsExcludingFiltered.Where(job => job.Status == SyncStatus.InProgress.ToString()).ToList();
            var syncJobsCount = filteredJobs.Count + activeInProgressJobs.Count;
            var totalSyncJobsCount = await _databaseSyncJobsRepository.GetSyncJobCountAsync(SyncStatus.All);
            _telemetryClient.TrackMetric(nameof(Metric.SyncJobsCount), syncJobsCount);
            _telemetryClient.TrackMetric(nameof(Metric.TotalSyncJobsCount), totalSyncJobsCount);
            var jobTriggerThresholdExceeded = HasJobTriggerThresholdExceeded(syncJobsCount, totalSyncJobsCount);
            return (filteredJobs, jobTriggerThresholdExceeded, _jobTriggerConfig.JobCountThreshold);
        }
        public async Task<string> GetDestinationNameAsync(SyncJob job)
        {
            var destination = (await ParseDestinationAsync(job));

            // Try to get the name from the DestinationNames table first

            var destinationName = await _databaseDestinationAttributesRepository.GetDestinationName(job);
            if(destinationName != null)
            {
                return destinationName;
            }

            if (destination.Type == "TeamsChannelMembership")
            {
                var channel = new AzureADTeamsChannel
                {
                    ObjectId = destination.Value.ObjectId,
                    ChannelId = (destination.Value as TeamsChannelDestinationValue).ChannelId
                };

                return await _teamsChannelRepository.GetTeamsChannelNameAsync(channel);
            }
            else if (destination.Type == "GroupMembership")
            {
                var objectId = destination.Value.ObjectId;
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
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = job.RunId,
                Message = $"Sent message {message.MessageId} to service bus notifications queue "

            });

        }
        
        public async Task UpdateSyncJobAsync(SyncStatus? status, SyncJob job)
        {
            if (status == SyncStatus.InProgress)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = job.RunId,
                    Message = $"Starting job."
                });

                job.LastSuccessfulStartTime = DateTime.UtcNow;
            }

            if (status == SyncStatus.StuckInProgress)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = job.RunId,
                    Message = $"Restarting job stuck in InProgress."
                });

                job.LastRunTime = DateTime.UtcNow;
                job.LastSuccessfulStartTime = DateTime.UtcNow;
            }

            await _databaseSyncJobsRepository.UpdateSyncJobStatusAsync(new[] { job }, status);
        }
        public async Task SendMessageAsync(SyncJob job)
        {
            await _serviceBusTopicsRepository.AddMessageAsync(job);
        }
        public async Task<DestinationVerifierResult> DestinationExistsAndGMMCanWriteToItAsync(SyncJob job)
        {
            var destinationType = (await ParseDestinationAsync(job)).Type;

            if (destinationType == "TeamsChannelMembership")
                return await TeamsChannelExistsAndGMMCanWriteToItAsync(job);
            else if (destinationType == "GroupMembership")
                return await GroupExistsAndGMMCanWriteToItAsync(job);
            else
                return DestinationVerifierResult.NotFound;
        }
        public async Task<List<string>> GetGroupEndpointsAsync(SyncJob job)
        {
            var destinationObjectId = (await ParseDestinationAsync(job)).Value.ObjectId;
            return await _graphGroupRepository.GetGroupEndpointsAsync(destinationObjectId);
        }
        public async Task<(bool IsValid, string DestinationObject)> ParseAndValidateDestinationAsync(SyncJob syncJob)
        {
            var destinationObject = await ParseDestinationAsync(syncJob);

            if (destinationObject == null)
            {
                return (false, null);
            }
            else
            {
                var options = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
                var serializedDestinationObject = JsonSerializer.Serialize(destinationObject, options);

                return (true, serializedDestinationObject);
            }
        }
        public async Task<DestinationObject> ParseDestinationAsync(SyncJob syncJob)
        {
            if (string.IsNullOrWhiteSpace(syncJob.Destination)) return null;

            JObject destinationQuery = JArray.Parse(syncJob.Destination)[0] as JObject;
            Guid objectIdGuid;
            string type;

            if (destinationQuery["value"] == null ||
                destinationQuery["type"] == null ||
                destinationQuery["value"].SelectToken("objectId") == null ||
                !Guid.TryParse(destinationQuery["value"]["objectId"].Value<string>(), out objectIdGuid)) return null;

            type = destinationQuery["type"].Value<string>();

            if (type == "TeamsChannelMembership")
            {
                if (destinationQuery["value"].SelectToken("channelId") == null) return null;

                return new DestinationObject
                {
                    Type = type,
                    Value = new TeamsChannelDestinationValue
                    {
                        ObjectId = objectIdGuid,
                        ChannelId = destinationQuery["value"]["channelId"].Value<string>()
                    }
                };
            }
            else if (type == "GroupMembership")
            {
                return new DestinationObject
                {
                    Type = type,
                    Value = new GroupDestinationValue
                    {
                        ObjectId = objectIdGuid,
                    }
                };
            }
            else { return null; }
        }
        private IEnumerable<SyncJob> ApplyJobTriggerFilters(IEnumerable<SyncJob> jobs)
        {
            var allNonDryRunSyncJobs = jobs.Where(x => ((DateTime.UtcNow - x.LastRunTime) > TimeSpan.FromHours(x.Period)) && x.IsDryRunEnabled == false && x.Status != SyncStatus.InProgress.ToString());
            var allDryRunSyncJobs = jobs.Where(x => ((DateTime.UtcNow - x.DryRunTimeStamp) > TimeSpan.FromHours(x.Period)) && x.IsDryRunEnabled == true && x.Status != SyncStatus.InProgress.ToString());
            var inProgressSyncJobs = jobs.Where(x => ((DateTime.UtcNow - x.LastSuccessfulStartTime) > TimeSpan.FromHours(x.Period)) && x.Status == SyncStatus.InProgress.ToString());
            return allNonDryRunSyncJobs.Concat(allDryRunSyncJobs).Concat(inProgressSyncJobs);
        }
        private bool HasJobTriggerThresholdExceeded(int syncJobsCount, int totalSyncJobsCount)
        {
            if (syncJobsCount < _jobTriggerConfig.JobCountThreshold)
            {
                return false;
            }
            else if (syncJobsCount >= _jobTriggerConfig.JobCountThreshold)
            {
                double percentage = ((double)syncJobsCount / totalSyncJobsCount) * 100;

                if (percentage >= _jobTriggerConfig.JobPercentThreshold)
                {
                    return true;
                }
            }
            return false;
        }
        private async Task<DestinationVerifierResult> GroupExistsAndGMMCanWriteToItAsync(SyncJob job)
        {
            var groupId = (await ParseDestinationAsync(job)).Value.ObjectId;

            if (!(await CheckGroupExists(job, groupId)))
                return DestinationVerifierResult.NotFound;
            if (!_jobTriggerConfig.GMMHasGroupReadWriteAllPermissions && !(await CheckGMMIsGroupOwner(job, groupId)))
                return DestinationVerifierResult.NotOwnedByGMM;
            return DestinationVerifierResult.Success;
        }
        private async Task<DestinationVerifierResult> TeamsChannelExistsAndGMMCanWriteToItAsync(SyncJob job)
        {
            var destinationObject = await ParseDestinationAsync(job);
            var channel = new AzureADTeamsChannel
            {
                ObjectId = destinationObject.Value.ObjectId,
                ChannelId = (destinationObject.Value as TeamsChannelDestinationValue).ChannelId
            };

            if (!await CheckTeamExists(job, channel))
                return DestinationVerifierResult.NotFound;
            if (!await CheckGMMIsTeamOwner(job, channel))
                return DestinationVerifierResult.NotOwnedByGMM;
            if (!await CheckChannelExists(job, channel))
                return DestinationVerifierResult.NotFound;
            if (!await CheckGMMIsChannelOwner(job, channel))
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
        private async Task<bool> CheckGMMIsTeamOwner(SyncJob job, AzureADTeamsChannel channel)
        {
            return await CheckAndLogAsync(job, $"GMM ownership of team {channel.ObjectId}",
                () => _graphGroupRepository.IsServiceAccountOwnerOfGroupAsync(_gmmTeamsChannelServiceAccountId, channel.ObjectId));
        }
        private async Task<bool> CheckGMMIsChannelOwner(SyncJob job, AzureADTeamsChannel channel)
        {
            return await CheckAndLogAsync(job, $"GMM ownership of channel {channel.ChannelId} in team {channel.ObjectId}",
                () => _teamsChannelRepository.IsServiceAccountOwnerOfChannelAsync(_gmmTeamsChannelServiceAccountId, channel, job.RunId));
        }
        private async Task<bool> CheckAndLogAsync(SyncJob job, string checkDescription, Func<Task<bool>> checkFunc)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { RunId = job.RunId, Message = $"Checking: {checkDescription} exists." });
            bool result = await checkFunc();
            string resultMessage = result ? "passed" : "failed";
            await _loggingRepository.LogMessageAsync(new LogMessage { RunId = job.RunId, Message = $"Check {resultMessage}: {checkDescription} {(result ? "exists" : "does not exist")}." });
            return result;
        }

    }
}
