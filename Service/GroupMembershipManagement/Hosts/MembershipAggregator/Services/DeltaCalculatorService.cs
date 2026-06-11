// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator;
using MembershipAggregator.Services.Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class DeltaCalculatorService : IDeltaCalculatorService
    {

        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly ILogger<DeltaCalculatorService> _logger;
        private readonly IGraphAPIService _graphAPIService;
        private readonly IThresholdConfig _thresholdConfig;
        private readonly INotificationRepository _notificationRepository;
        private readonly bool _isDryRunEnabled;
        private readonly IThresholdNotificationConfig _thresholdNotificationConfig;
        private readonly TelemetryClient _telemetryClient;
        private readonly IServiceBusQueueRepository _notificationsQueueRepository;

        public DeltaCalculatorService(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseGroupsRepository databaseGroupsRepository,
            IDatabaseChannelsRepository databaseChannelsRepository,
            ILogger<DeltaCalculatorService> logger,
            IGraphAPIService graphAPIService,
            IDryRunValue dryRun,
            IThresholdConfig thresholdConfig,
            IThresholdNotificationConfig thresholdNotificationConfig,
            INotificationRepository notificationRepository,
            IServiceBusQueueRepository notificationsQueueRepository,
            TelemetryClient telemetryClient
            )
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService));
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
            _thresholdNotificationConfig = thresholdNotificationConfig ?? throw new ArgumentNullException(nameof(thresholdNotificationConfig));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _isDryRunEnabled = dryRun != null && dryRun.DryRunEnabled;
            _notificationsQueueRepository = notificationsQueueRepository ?? throw new ArgumentNullException(nameof(notificationsQueueRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
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

        public async Task<DeltaResponse> CalculateDifferenceAsync(GroupMembership sourceMembership, GroupMembership destinationMembership)
        {
            var deltaResponse = new DeltaResponse
            {
                MembershipDeltaStatus = MembershipDeltaStatus.Ok,
                MembersToAdd = new List<AzureADUser>(),
                MembersToRemove = new List<AzureADUser>()
            };

            var job = await _syncJobRepository.GetSyncJobAsync(sourceMembership.SyncJobId);
            if (job == null)
            {
                _logger.SyncJobNotFound(sourceMembership.SyncJobId);
                deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.Error;
                return deltaResponse;
            }

            var groupId = await GetGroupIdAsync(job);
            var isDryRunSync = job.IsDryRunEnabled || sourceMembership.MembershipObtainerDryRunEnabled || _isDryRunEnabled;

            _logger.DryRunConfiguration(isDryRunSync);

            _logger.ProcessingSyncJob(sourceMembership.SyncJobId);

            _logger.JobStatusInfo(groupId, job.Status);

            var fromto = $"to {sourceMembership.Destination}";
            var groupExistsResult = await _graphAPIService.GroupExistsAsync(sourceMembership.Destination.ObjectId);
            if (!groupExistsResult.Result)
            {
                _logger.DestinationGroupNotExists(fromto, sourceMembership.Destination.ToString());

                deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.Error;
                return deltaResponse;
            }

            if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Ok)
            {
                var delta = await CalculateDeltaAsync(sourceMembership, destinationMembership, fromto, job);
                var isInitialSync = job.LastRunTime == SqlDateTime.MinValue.Value;
                var threshold = isInitialSync ? new ThresholdResult() : await CalculateThresholdAsync(job, groupId, delta.Delta, delta.TotalMembersCount, sourceMembership.RunId);

                deltaResponse.MembersToAdd = delta.Delta.ToAdd;
                deltaResponse.MembersToRemove = delta.Delta.ToRemove;

                if (threshold.IsThresholdExceeded)
                {
                    deltaResponse.MembershipDeltaStatus = job.IgnoreThresholdOnce ? MembershipDeltaStatus.Ok : MembershipDeltaStatus.ThresholdExceeded;
                    TrackThresholdViolationEvent(groupId);

                    if (job.IgnoreThresholdOnce)
                        await LogIgnoreThresholdOnceAsync(job, sourceMembership.RunId);
                    else if (job.AllowEmptyDestination && (delta.Delta.ToAdd.Count > 0 && delta.TotalMembersCount == 0))
                    {
                        deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.Ok;
                        await LogAllowEmptyDestinationAsync(job, sourceMembership.RunId);
                    }
                    else
                    {
                        await SendThresholdNotificationAsync(threshold, job, groupId, sourceMembership.RunId);
                    }

                    return deltaResponse;
                }
                else if (job.ThresholdViolations > 0)
                {
                    await CloseUnresolvedThresholdNotificationAsync(job);
                }

                if (isDryRunSync)
                {
                    deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.DryRun;
                    return deltaResponse;
                }

                if (deltaResponse.MembersToAdd.Count == 0 && deltaResponse.MembersToRemove.Count == 0)
                    deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.NoChanges;
            }

            return deltaResponse;
        }

        private async Task<(MembershipDelta<AzureADUser> Delta, int TotalMembersCount)> CalculateDeltaAsync(
                                                                                            GroupMembership sourceMembership,
                                                                                            GroupMembership destinationMembership,
                                                                                            string fromto,
                                                                                            SyncJob job)
        {
            _logger.CalculatingMembershipDifference(fromto, destinationMembership?.SourceMembers?.Count ?? 0);

            var stopwatch = Stopwatch.StartNew();
            var sourceMembers = sourceMembership?.SourceMembers ?? new List<AzureADUser>();
            var destinationMembers = destinationMembership?.SourceMembers ?? new List<AzureADUser>();

            var sourceSet = new HashSet<AzureADUser>(sourceMembers);
            var destinationSet = new HashSet<AzureADUser>(destinationMembers);

            sourceSet.ExceptWith(destinationMembers);
            destinationSet.ExceptWith(sourceMembers);

            var toAdd = sourceSet.ToList();
            toAdd.ForEach(x => x.MembershipAction = MembershipAction.Add);

            var toRemove = destinationSet.ToList();
            toRemove.ForEach(x => x.MembershipAction = MembershipAction.Remove);

            var delta = new MembershipDelta<AzureADUser>(toAdd, toRemove);

            stopwatch.Stop();

            _logger.CalculatedMembershipDifference(fromto, stopwatch.Elapsed.TotalSeconds, delta.ToAdd.Count, delta.ToRemove.Count);

            var destinationMemberCount = destinationMembers.Count;

            return (delta, destinationMemberCount);
        }

        private async Task<ThresholdResult> CalculateThresholdAsync(SyncJob job, Guid groupId, MembershipDelta<AzureADUser> delta, int totalMembersCount, Guid runId)
        {
            double percentageIncrease = 0;
            double percentageDecrease = 0;
            bool isAdditionsThresholdExceeded = false;
            bool isRemovalsThresholdExceeded = false;
            totalMembersCount = totalMembersCount == 0 ? 1 : totalMembersCount;

            if (job.ThresholdPercentageForAdditions >= 0)
            {
                percentageIncrease = (double)delta.ToAdd.Count / totalMembersCount * 100;
                isAdditionsThresholdExceeded = percentageIncrease > job.ThresholdPercentageForAdditions;

                if (isAdditionsThresholdExceeded)
                {
                    _logger.AdditionsThresholdExceeded(groupId, percentageIncrease, job.ThresholdPercentageForAdditions);
                }
            }

            if (job.ThresholdPercentageForRemovals >= 0)
            {
                percentageDecrease = (double)delta.ToRemove.Count / totalMembersCount * 100;
                isRemovalsThresholdExceeded = percentageDecrease > job.ThresholdPercentageForRemovals;

                if (isRemovalsThresholdExceeded)
                {
                    _logger.RemovalsThresholdExceeded(groupId, percentageDecrease, job.ThresholdPercentageForRemovals);
                }
            }

            return new ThresholdResult
            {
                IncreaseThresholdPercentage = percentageIncrease,
                DecreaseThresholdPercentage = percentageDecrease,
                DeltaToAddCount = delta.ToAdd.Count,
                DeltaToRemoveCount = delta.ToRemove.Count,
                IsAdditionsThresholdExceeded = isAdditionsThresholdExceeded,
                IsRemovalsThresholdExceeded = isRemovalsThresholdExceeded
            };
        }

        private async Task LogIgnoreThresholdOnceAsync(SyncJob job, Guid runId)
        {
            _logger.IgnoreThresholdOnceSync(job.IgnoreThresholdOnce);
        }

        private async Task LogAllowEmptyDestinationAsync(SyncJob job, Guid runId)
        {
            _logger.AllowEmptyDestinationSync(job.AllowEmptyDestination);
        }
        private async Task SendThresholdNotificationAsync(ThresholdResult threshold, SyncJob job, Guid groupId, Guid runId)
        {
            var currentThresholdViolations = job.ThresholdViolations + 1;
            var sendNotification = currentThresholdViolations >= _thresholdConfig.NumberOfThresholdViolationsToNotify;
            var sendDisableJobNotification = currentThresholdViolations == _thresholdConfig.NumberOfThresholdViolationsToDisableJob;

            var groupName = await _graphAPIService.GetGroupNameAsync(groupId);
            _logger.ThresholdExceededNoChanges(groupName, groupId);

            if (!sendNotification && !sendDisableJobNotification)
            {
                return;
            }
            await SendThresholdNotification(threshold, job, sendDisableJobNotification, groupName);
        }
        private async Task SendThresholdNotification(ThresholdResult threshold, SyncJob job, bool sendDisableJobNotification, string groupName)
        {
            var messageContent = new Dictionary<string, Object>
            {
                { "ThresholdResult", threshold },
                { "SyncJob", job },
                { "SendDisableJobNotification", sendDisableJobNotification }
            };

            if (!_thresholdNotificationConfig.IsThresholdNotificationEnabled)
            {
                messageContent.Add("GroupName", groupName);
            }
            var body = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(messageContent));

            var messageType = _thresholdNotificationConfig.IsThresholdNotificationEnabled
                ? NotificationMessageType.ThresholdNotification
                : NotificationMessageType.NormalThresholdNotification;

            var messageId = $"{job.Id}_{job.RunId}_{messageType}";

            var message = new ServiceBusMessage
            {
                MessageId = messageId,
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", messageType.ToString());
            await _notificationsQueueRepository.SendMessageAsync(message);
            _logger.SentNotificationQueueMessage(message.MessageId);
        }
        private async Task CloseUnresolvedThresholdNotificationAsync(SyncJob job)
        {
            if (_thresholdNotificationConfig.IsThresholdNotificationEnabled)
            {
                var thresholdNotification = await _notificationRepository.GetThresholdNotificationBySyncJobIdAsync(job.Id);
                if (thresholdNotification != null && thresholdNotification.Status != ThresholdNotificationStatus.Resolved)
                {
                    thresholdNotification.Resolution = ThresholdNotificationResolution.SelfCorrected;
                    thresholdNotification.ResolvedBy = "N/A";
                    thresholdNotification.ResolvedTime = DateTime.UtcNow;
                    thresholdNotification.Status = ThresholdNotificationStatus.Resolved;

                    await _notificationRepository.SaveNotificationAsync(thresholdNotification);
                }
            }
        }
        private void TrackThresholdViolationEvent(Guid groupId)
        {
            var thresholdViolationEvent = new Dictionary<string, string>
            {
                { "TargetGroupId", groupId.ToString() }
            };
            _telemetryClient.TrackEvent("ThresholdViolation", thresholdViolationEvent);
        }
    }
}
