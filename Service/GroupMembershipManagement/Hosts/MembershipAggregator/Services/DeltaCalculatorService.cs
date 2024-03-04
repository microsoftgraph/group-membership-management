// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.ServiceBus;
using Models;
using Models.ThresholdNotifications;
using Models.Notifications;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using System.Data.SqlTypes;

namespace Services
{
    public class DeltaCalculatorService : IDeltaCalculatorService
    {

        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphAPIService _graphAPIService;
        private readonly IThresholdConfig _thresholdConfig;
        private readonly INotificationRepository _notificationRepository;
        private readonly bool _isDryRunEnabled;
        private readonly IThresholdNotificationConfig _thresholdNotificationConfig;
        private readonly TelemetryClient _telemetryClient;
        private readonly IServiceBusQueueRepository _notificationsQueueRepository;

        private Guid _runId;
        public Guid RunId
        {
            get { return _runId; }
            set
            {
                _runId = value;
                _graphAPIService.RunId = value;
            }
        }

        public DeltaCalculatorService(
            IDatabaseSyncJobsRepository syncJobRepository,
            ILoggingRepository loggingRepository,
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
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService));
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
            _thresholdNotificationConfig = thresholdNotificationConfig ?? throw new ArgumentNullException(nameof(thresholdNotificationConfig));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _isDryRunEnabled = dryRun != null && dryRun.DryRunEnabled;
            _notificationsQueueRepository = notificationsQueueRepository ?? throw new ArgumentNullException(nameof(notificationsQueueRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
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
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sync job : Id {sourceMembership.SyncJobId} was not found!", RunId = sourceMembership.RunId });
                deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.Error;
                return deltaResponse;
            }

            var isDryRunSync = _loggingRepository.DryRun = job.IsDryRunEnabled || sourceMembership.MembershipObtainerDryRunEnabled || _isDryRunEnabled;

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"The Dry Run Enabled configuration is currently set to {isDryRunSync}. " +
                          $"We will not be syncing members if any of the 3 Dry Run Enabled configurations is set to True.",
                RunId = sourceMembership.RunId
            });

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Processing sync job : Id {sourceMembership.SyncJobId}",
                RunId = sourceMembership.RunId
            });

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{job.TargetOfficeGroupId} job's status is {job.Status}.",
                RunId = sourceMembership.RunId
            });

            var fromto = $"to {sourceMembership.Destination}";
            var groupExistsResult = await _graphAPIService.GroupExistsAsync(sourceMembership.Destination.ObjectId, sourceMembership.RunId);
            if (!groupExistsResult.Result)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"When syncing {fromto}, destination group {sourceMembership.Destination} doesn't exist. Not syncing and marking as error.",
                    RunId = sourceMembership.RunId
                });

                deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.Error;
                return deltaResponse;
            }

            if (deltaResponse.MembershipDeltaStatus == MembershipDeltaStatus.Ok)
            {
                var delta = await CalculateDeltaAsync(sourceMembership, destinationMembership, fromto, job);
                var isInitialSync = job.LastRunTime == SqlDateTime.MinValue.Value;
                var threshold = isInitialSync ? new ThresholdResult() : await CalculateThresholdAsync(job, delta.Delta, delta.TotalMembersCount, sourceMembership.RunId);

                deltaResponse.MembersToAdd = delta.Delta.ToAdd;
                deltaResponse.MembersToRemove = delta.Delta.ToRemove;

                if (threshold.IsThresholdExceeded)
                {
                    deltaResponse.MembershipDeltaStatus = job.IgnoreThresholdOnce ? MembershipDeltaStatus.Ok : MembershipDeltaStatus.ThresholdExceeded;
                    TrackThresholdViolationEvent(job.TargetOfficeGroupId);

                    if (job.IgnoreThresholdOnce)
                        await LogIgnoreThresholdOnceAsync(job, sourceMembership.RunId);
                    else if (job.AllowEmptyDestination && (delta.Delta.ToAdd.Count > 0 && delta.TotalMembersCount == 0))
                    {
                        deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.Ok;
                        await LogAllowEmptyDestinationAsync(job, sourceMembership.RunId);
                    }
                    else
                    {
                        await SendThresholdNotificationAsync(threshold, job, sourceMembership.RunId);
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
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Calculating membership difference {fromto}. " +
                          $"Destination group has {destinationMembership.SourceMembers.Count} users.",
                RunId = sourceMembership.RunId
            });

            var stopwatch = Stopwatch.StartNew();
            var sourceSet = new HashSet<AzureADUser>(sourceMembership.SourceMembers);
            var destinationSet = new HashSet<AzureADUser>(destinationMembership.SourceMembers);

            sourceSet.ExceptWith(destinationMembership.SourceMembers);
            destinationSet.ExceptWith(sourceMembership.SourceMembers);

            var toAdd = sourceSet.ToList();
            toAdd.ForEach(x => x.MembershipAction = MembershipAction.Add);

            var toRemove = destinationSet.ToList();
            toRemove.ForEach(x => x.MembershipAction = MembershipAction.Remove);

            var delta = new MembershipDelta<AzureADUser>(toAdd, toRemove);

            stopwatch.Stop();

            await _loggingRepository.LogMessageAsync(
            new LogMessage
            {
                Message = $"Calculated membership difference {fromto} in {stopwatch.Elapsed.TotalSeconds} seconds. " +
                          $"Adding {delta.ToAdd.Count} users and removing {delta.ToRemove.Count}.",
                RunId = sourceMembership.RunId
            });

            return (delta, destinationMembership.SourceMembers.Count);
        }

        private async Task<ThresholdResult> CalculateThresholdAsync(SyncJob job, MembershipDelta<AzureADUser> delta, int totalMembersCount, Guid runId)
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
                    await _loggingRepository.LogMessageAsync(
                        new LogMessage
                        {
                            Message = $"Membership increase in {job.TargetOfficeGroupId} is {percentageIncrease}% " +
                                      $"and is greater than threshold value {job.ThresholdPercentageForAdditions}%",
                            RunId = runId
                        });
                }
            }

            if (job.ThresholdPercentageForRemovals >= 0)
            {
                percentageDecrease = (double)delta.ToRemove.Count / totalMembersCount * 100;
                isRemovalsThresholdExceeded = percentageDecrease > job.ThresholdPercentageForRemovals;

                if (isRemovalsThresholdExceeded)
                {
                    await _loggingRepository.LogMessageAsync(
                        new LogMessage
                        {
                            Message = $"Membership decrease in {job.TargetOfficeGroupId} is {percentageDecrease}% " +
                                      $"and is lesser than threshold value {job.ThresholdPercentageForRemovals}%",
                            RunId = runId
                        });
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
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Going to sync the job even though threshold exceeded because IgnoreThresholdOnce is currently set to {job.IgnoreThresholdOnce}.",
                RunId = runId
            });
        }

        private async Task LogAllowEmptyDestinationAsync(SyncJob job, Guid runId)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Going to sync the job even though threshold exceeded because AllowEmptyDestination is currently set to {job.AllowEmptyDestination}.",
                RunId = runId
            });
        }
        private async Task SendThresholdNotificationAsync(ThresholdResult threshold, SyncJob job, Guid runId)
        {
            var currentThresholdViolations = job.ThresholdViolations + 1;
            var sendNotification = currentThresholdViolations >= _thresholdConfig.NumberOfThresholdViolationsToNotify;
            var sendDisableJobNotification = currentThresholdViolations == _thresholdConfig.NumberOfThresholdViolationsToDisableJob;

            var groupName = await _graphAPIService.GetGroupNameAsync(job.TargetOfficeGroupId);
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Threshold exceeded, no changes made to group {groupName} ({job.TargetOfficeGroupId}). ",
                RunId = runId
            });

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
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Sent message {message.MessageId} to service bus notifications queue ",
                RunId = job.RunId
            });
        }   
        private async Task CloseUnresolvedThresholdNotificationAsync(SyncJob job)
        {
            if (_thresholdNotificationConfig.IsThresholdNotificationEnabled)
            {
                var thresholdNotification = await _notificationRepository.GetThresholdNotificationBySyncJobIdAsync(job.Id);
                if (thresholdNotification != null && thresholdNotification.Status != ThresholdNotificationStatus.Resolved)
                {
                    thresholdNotification.Resolution = ThresholdNotificationResolution.SelfCorrected;
                    thresholdNotification.ResolvedByUPN = "N/A";
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
