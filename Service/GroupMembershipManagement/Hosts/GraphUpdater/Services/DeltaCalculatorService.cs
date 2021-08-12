// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Services
{
    public class DeltaCalculatorService : IDeltaCalculatorService
    {
        private const string SyncThresholdBothEmailBody = "SyncThresholdBothEmailBody";
        private const string SyncThresholdIncreaseEmailBody = "SyncThresholdIncreaseEmailBody";
        private const string SyncThresholdDecreaseEmailBody = "SyncThresholdDecreaseEmailBody";

        private readonly IMembershipDifferenceCalculator<AzureADUser> _differenceCalculator;
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly IGraphUpdaterService _graphUpdaterService;
        private readonly bool _isGraphUpdaterDryRunEnabled;

        public DeltaCalculatorService(
            IMembershipDifferenceCalculator<AzureADUser> differenceCalculator,
            ISyncJobRepository syncJobRepository,
            ILoggingRepository loggingRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGraphUpdaterService graphUpdaterService,
            IDryRunValue dryRun
            )
        {
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _differenceCalculator = differenceCalculator ?? throw new ArgumentNullException(nameof(differenceCalculator));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _isGraphUpdaterDryRunEnabled = _loggingRepository.DryRun = dryRun != null ? dryRun.DryRunEnabled : throw new ArgumentNullException(nameof(dryRun));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        public async Task<DeltaResponse> CalculateDifferenceAsync(GroupMembership membership, List<AzureADUser> membersFromDestinationGroup)
        {
            SetupLoggingRepository(membership);

            var deltaResponse = new DeltaResponse
            {
                IsDryRunSync = _isGraphUpdaterDryRunEnabled
            };

            var fromto = $"to {membership.Destination}";
            var changeTo = SyncStatus.Idle;

            var job = await _syncJobRepository.GetSyncJobAsync(membership.SyncJobPartitionKey, membership.SyncJobRowKey);
            if (job == null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sync job : Partition key {membership.SyncJobPartitionKey}, Row key {membership.SyncJobRowKey} was not found!", RunId = membership.RunId });

                deltaResponse.GraphUpdaterStatus = GraphUpdaterStatus.Error;
                deltaResponse.SyncStatus = SyncStatus.Error;
                deltaResponse.IsDryRunSync = membership.MembershipObtainerDryRunEnabled || _isGraphUpdaterDryRunEnabled;
                return deltaResponse;
            }

            var isDryRunSync = _loggingRepository.DryRun = job.IsDryRunEnabled || membership.MembershipObtainerDryRunEnabled || _isGraphUpdaterDryRunEnabled;

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"The Dry Run Enabled configuration is currently set to {isDryRunSync}. We will not be syncing members if any of the 3 Dry Run Enabled configurations is set to True.", RunId = membership.RunId });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Processing sync job : Partition key {membership.SyncJobPartitionKey} , Row key {membership.SyncJobRowKey}", RunId = membership.RunId });
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{job.TargetOfficeGroupId} job's status is {job.Status}.", RunId = membership.RunId });

            var groupExistsResult = await _graphUpdaterService.GroupExistsAsync(membership.Destination.ObjectId, membership.RunId);
            if (!groupExistsResult.Result)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"When syncing {fromto}, destination group {membership.Destination} doesn't exist. Not syncing and marking as error.", RunId = membership.RunId });
                changeTo = SyncStatus.Error;
            }

            if (changeTo == SyncStatus.Idle)
            {
                var delta = await CalculateDeltaAsync(membership, fromto, membersFromDestinationGroup);
                var isInitialSync = job.LastRunTime == DateTime.FromFileTimeUtc(0);
                var threshold = isInitialSync ? new ThresholdResult() : await CalculateThresholdAsync(job, delta.Delta, delta.TotalMembersCount, membership.RunId);

                if (threshold.IsThresholdExceeded)
                {
                    await SendThresholdNotificationAsync(threshold, job, membership.RunId);

                    deltaResponse.GraphUpdaterStatus = GraphUpdaterStatus.ThresholdExceeded;
                    deltaResponse.MembersToAdd = delta.Delta.ToAdd;
                    deltaResponse.MembersToRemove = delta.Delta.ToRemove;
                    deltaResponse.SyncStatus = SyncStatus.Idle;
                    deltaResponse.IsDryRunSync = isDryRunSync;
                    return deltaResponse;
                }

                deltaResponse.MembersToAdd = delta.Delta.ToAdd;
                deltaResponse.MembersToRemove = delta.Delta.ToRemove;
                deltaResponse.GraphUpdaterStatus = GraphUpdaterStatus.Ok;
                deltaResponse.SyncStatus = SyncStatus.Idle;
                deltaResponse.IsInitialSync = isInitialSync;
                deltaResponse.Requestor = job.Requestor;
                deltaResponse.IsDryRunSync = isDryRunSync;
            }

            if (changeTo == SyncStatus.Error)
            {
                return new DeltaResponse
                {
                    GraphUpdaterStatus = GraphUpdaterStatus.Error,
                    SyncStatus = SyncStatus.Error,
                    IsDryRunSync = isDryRunSync
            };
            }

            return deltaResponse;
        }

        private async Task<(MembershipDelta<AzureADUser> Delta, int TotalMembersCount)> CalculateDeltaAsync(GroupMembership membership, string fromto, List<AzureADUser> destinationMembers)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Calculating membership difference {fromto}. Destination group has {destinationMembers.Count} users.", RunId = membership.RunId });

            var stopwatch = Stopwatch.StartNew();
            var delta = _differenceCalculator.CalculateDifference(membership.SourceMembers, destinationMembers);
            stopwatch.Stop();

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"Calculated membership difference {fromto} in {stopwatch.Elapsed.TotalSeconds} seconds. " +
                                                    $"Adding {delta.ToAdd.Count} users and removing {delta.ToRemove.Count}.",
                    RunId = membership.RunId
                });

            return (delta, destinationMembers.Count);
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
                IsAdditionsThresholdExceeded = isAdditionsThresholdExceeded,
                IsRemovalsThresholdExceeded = isRemovalsThresholdExceeded
            };
        }

        private async Task SendThresholdNotificationAsync(ThresholdResult threshold, SyncJob job, Guid runId)
        {
            string groupName = await _graphUpdaterService.GetGroupNameAsync(job.TargetOfficeGroupId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Threshold exceeded, no changes made to group {groupName} ({job.TargetOfficeGroupId}). ", RunId = runId });

            string contentTemplate;
            string[] additionalContent;
            if (threshold.IsAdditionsThresholdExceeded && threshold.IsRemovalsThresholdExceeded)
            {
                contentTemplate = SyncThresholdBothEmailBody;
                additionalContent = new[]
                {
                      groupName,
                      job.TargetOfficeGroupId.ToString(),
                      job.ThresholdPercentageForAdditions.ToString(),
                      threshold.IncreaseThresholdPercentage.ToString("F2"),
                      job.ThresholdPercentageForRemovals.ToString(),
                      threshold.DecreaseThresholdPercentage.ToString("F2")
                    };
            }
            else if (threshold.IsAdditionsThresholdExceeded)
            {
                contentTemplate = SyncThresholdIncreaseEmailBody;
                additionalContent = new[]
                {
                      groupName,
                      job.TargetOfficeGroupId.ToString(),
                      job.ThresholdPercentageForAdditions.ToString(),
                      threshold.IncreaseThresholdPercentage.ToString("F2")
                    };
            }
            else
            {
                contentTemplate = SyncThresholdDecreaseEmailBody;
                additionalContent = new[]
                {
                      groupName,
                      job.TargetOfficeGroupId.ToString(),
                      job.ThresholdPercentageForRemovals.ToString(),
                      threshold.DecreaseThresholdPercentage.ToString("F2")
                    };
            }

            await _graphUpdaterService.SendEmailAsync(_emailSenderAndRecipients.SyncDisabledCCAddresses, contentTemplate, additionalContent, runId);
        }

        private void SetupLoggingRepository(GroupMembership membership)
        {
            _loggingRepository.DryRun = _isGraphUpdaterDryRunEnabled;
            _loggingRepository.SyncJobProperties = new Dictionary<string, string>
            {
                { "partitionKey", membership.SyncJobPartitionKey },
                { "rowKey", membership.SyncJobRowKey },
                { "targetOfficeGroupId", membership.Destination.ObjectId.ToString() }
            };
        }
    }
}
