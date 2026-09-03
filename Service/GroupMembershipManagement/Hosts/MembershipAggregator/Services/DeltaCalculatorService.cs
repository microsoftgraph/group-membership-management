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
using Repositories.Contracts.DestinationResolution;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Threading.Tasks;

namespace Services
{
    public class DeltaCalculatorService : IDeltaCalculatorService
    {

        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDestinationResolver _destinationResolver;
        private readonly ILogger<DeltaCalculatorService> _logger;
        private readonly IGraphAPIService _graphAPIService;
        private readonly INotificationRepository _notificationRepository;
        private readonly bool _isDryRunEnabled;
        private readonly TelemetryClient _telemetryClient;
        private readonly IServiceBusQueueRepository _notificationsQueueRepository;

        public DeltaCalculatorService(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDestinationResolver destinationResolver,
            ILogger<DeltaCalculatorService> logger,
            IGraphAPIService graphAPIService,
            IDryRunValue dryRun,
            INotificationRepository notificationRepository,
            IServiceBusQueueRepository notificationsQueueRepository,
            TelemetryClient telemetryClient
            )
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _destinationResolver = destinationResolver ?? throw new ArgumentNullException(nameof(destinationResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _isDryRunEnabled = dryRun != null && dryRun.DryRunEnabled;
            _notificationsQueueRepository = notificationsQueueRepository ?? throw new ArgumentNullException(nameof(notificationsQueueRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        public async Task<Guid> GetGroupIdAsync(SyncJob syncJob)
        {
            var destination = await _destinationResolver.ResolveAsync(syncJob);
            return destination switch
            {
                ResolvedGroupDestination groupDestination => groupDestination.ObjectId,
                ResolvedTeamsChannelDestination channelDestination => channelDestination.TeamObjectId,
                _ => Guid.Empty
            };
        }

        public async Task<string> GetChannelIdAsync(SyncJob syncJob)
        {
            var destination = await _destinationResolver.ResolveAsync(syncJob);
            return destination is ResolvedTeamsChannelDestination channelDestination ? channelDestination.ChannelId : string.Empty;
        }

        public async Task<DeltaResponse> CalculateDifferenceAsync(
            GroupMembership sourceMembership,
            MembershipDeltaSummary deltaSummary)
        {
            if (sourceMembership == null) throw new ArgumentNullException(nameof(sourceMembership));
            if (deltaSummary == null) throw new ArgumentNullException(nameof(deltaSummary));

            var deltaResponse = new DeltaResponse
            {
                MembershipDeltaStatus = MembershipDeltaStatus.Ok,
                MembersToAddCount = deltaSummary.MembersToAddCount,
                MembersToRemoveCount = deltaSummary.MembersToRemoveCount
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
                var isInitialSync = job.LastRunTime == SqlDateTime.MinValue.Value;
                var threshold = isInitialSync
                    ? new ThresholdResult()
                    : CalculateThreshold(job, groupId, deltaSummary);

                if (threshold.IsThresholdExceeded)
                {
                    deltaResponse.MembershipDeltaStatus = job.IgnoreThresholdOnce ? MembershipDeltaStatus.Ok : MembershipDeltaStatus.ThresholdExceeded;
                    TrackThresholdViolationEvent(groupId);

                    if (job.IgnoreThresholdOnce)
                        await LogIgnoreThresholdOnceAsync(job, sourceMembership.RunId);
                    else if (job.AllowEmptyDestination
                        && deltaSummary.MembersToAddCount > 0
                        && deltaSummary.DestinationMemberCount == 0)
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
                else
                {
                    await CloseUnresolvedThresholdNotificationAsync(job);
                }

                if (isDryRunSync)
                {
                    deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.DryRun;
                    return deltaResponse;
                }

                if (deltaResponse.MembersToAddCount == 0
                    && deltaResponse.MembersToRemoveCount == 0)
                {
                    deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.NoChanges;
                }
            }

            return deltaResponse;
        }

        private ThresholdResult CalculateThreshold(
            SyncJob job,
            Guid groupId,
            MembershipDeltaSummary deltaSummary)
        {
            double percentageIncrease = 0;
            double percentageDecrease = 0;
            bool isAdditionsThresholdExceeded = false;
            bool isRemovalsThresholdExceeded = false;
            var thresholdDenominator = deltaSummary.DestinationMemberCount == 0
                ? 1
                : deltaSummary.DestinationMemberCount;

            if (job.ThresholdPercentageForAdditions >= 0)
            {
                percentageIncrease =
                    (double)deltaSummary.MembersToAddCount / thresholdDenominator * 100;
                isAdditionsThresholdExceeded = percentageIncrease > job.ThresholdPercentageForAdditions;

                if (isAdditionsThresholdExceeded)
                {
                    _logger.AdditionsThresholdExceeded(groupId, percentageIncrease, job.ThresholdPercentageForAdditions);
                }
            }

            if (job.ThresholdPercentageForRemovals >= 0)
            {
                percentageDecrease =
                    (double)deltaSummary.MembersToRemoveCount / thresholdDenominator * 100;
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
                DeltaToAddCount = deltaSummary.MembersToAddCount,
                DeltaToRemoveCount = deltaSummary.MembersToRemoveCount,
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
            var groupName = await _graphAPIService.GetGroupNameAsync(groupId);
            _logger.ThresholdExceededNoChanges(groupName, groupId);

            await SendThresholdNotification(threshold, job);
        }
        private async Task SendThresholdNotification(ThresholdResult threshold, SyncJob job)
        {
            var messageContent = new Dictionary<string, Object>
            {
                { "ThresholdResult", threshold },
                { "SyncJob", job },
                { "SendDisableJobNotification", true }
            };

            var body = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(messageContent));

            var messageType = NotificationMessageType.ThresholdNotification;

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
