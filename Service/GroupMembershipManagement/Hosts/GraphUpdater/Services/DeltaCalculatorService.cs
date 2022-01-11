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
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class DeltaCalculatorService : IDeltaCalculatorService
    {
        private const string SyncThresholdBothEmailBody = "SyncThresholdBothEmailBody";
        private const string SyncThresholdEmailSubject = "SyncThresholdEmailSubject";
        private const string SyncThresholdDisablingJobEmailSubject = "SyncThresholdDisablingJobEmailSubject";
        private const string SyncJobDisabledEmailBody = "SyncJobDisabledEmailBody";
        private const string IncreaseThresholdMessage = "IncreaseThresholdMessage";
        private const string DecreaseThresholdMessage = "DecreaseThresholdMessage";

        private readonly IMembershipDifferenceCalculator<AzureADUser> _differenceCalculator;
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly IGraphUpdaterService _graphUpdaterService;
        private readonly IThresholdConfig _thresholdConfig;
        private readonly IGMMResources _gmmResources;
        private readonly ILocalizationRepository _localizationRepository;
        private readonly bool _isGraphUpdaterDryRunEnabled;

        public DeltaCalculatorService(
            IMembershipDifferenceCalculator<AzureADUser> differenceCalculator,
            ISyncJobRepository syncJobRepository,
            ILoggingRepository loggingRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGraphUpdaterService graphUpdaterService,
            IDryRunValue dryRun,
            IThresholdConfig thresholdConfig,
            IGMMResources gmmResources,
            ILocalizationRepository localizationRepository
            )
        {
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _differenceCalculator = differenceCalculator ?? throw new ArgumentNullException(nameof(differenceCalculator));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _isGraphUpdaterDryRunEnabled = _loggingRepository.DryRun = dryRun != null ? dryRun.DryRunEnabled : throw new ArgumentNullException(nameof(dryRun));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
        }

        public async Task<DeltaResponse> CalculateDifferenceAsync(GroupMembership membership, List<AzureADUser> membersFromDestinationGroup)
        {
            var deltaResponse = new DeltaResponse
            {
                IsDryRunSync = _isGraphUpdaterDryRunEnabled
            };

            var fromto = $"to {membership.Destination}";
            var changeTo = SyncStatus.Idle;

            var job = await _syncJobRepository.GetSyncJobAsync(membership.SyncJobPartitionKey, membership.SyncJobRowKey);

            SetupLoggingRepository(membership, job);

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

                deltaResponse.MembersToAdd = delta.Delta.ToAdd;
                deltaResponse.MembersToRemove = delta.Delta.ToRemove;
                deltaResponse.SyncStatus = SyncStatus.Idle;
                deltaResponse.IsInitialSync = isInitialSync;
                deltaResponse.IsDryRunSync = isDryRunSync;
                deltaResponse.Requestor = job.Requestor;
                deltaResponse.SyncJobType = job.Type;
                deltaResponse.Timestamp = job.Timestamp;
                deltaResponse.GraphUpdaterStatus = GraphUpdaterStatus.Ok;

                if (threshold.IsThresholdExceeded)
                {
                    deltaResponse.GraphUpdaterStatus = GraphUpdaterStatus.ThresholdExceeded;

                    await SendThresholdNotificationAsync(threshold, job, membership.RunId);

                    return deltaResponse;
                }
            }

            if (changeTo == SyncStatus.Error)
            {
                return new DeltaResponse
                {
                    GraphUpdaterStatus = GraphUpdaterStatus.Error,
                    SyncStatus = SyncStatus.Error,
                    IsDryRunSync = isDryRunSync,
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
            var currentThresholdViolations = job.ThresholdViolations + 1;
            var followUpLimit = _thresholdConfig.NumberOfThresholdViolationsToNotify + _thresholdConfig.NumberOfThresholdViolationsFollowUps;
            var sendNotification = currentThresholdViolations >= _thresholdConfig.NumberOfThresholdViolationsToNotify && currentThresholdViolations <= followUpLimit;
            var sendDisableJobNotification = currentThresholdViolations == _thresholdConfig.NumberOfThresholdViolationsToDisableJob;

            if (!sendNotification && !sendDisableJobNotification)
            {
                return;
            }

            var groupName = await _graphUpdaterService.GetGroupNameAsync(job.TargetOfficeGroupId);
            var emailSubject = SyncThresholdEmailSubject;

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Threshold exceeded, no changes made to group {groupName} ({job.TargetOfficeGroupId}). ", RunId = runId });

            string contentTemplate;
            string[] additionalContent;
            string[] additionalSubjectContent = new[] { groupName };

            var thresholdEmail = GetThresholdEmail(groupName, threshold, job);
            contentTemplate = thresholdEmail.ContentTemplate;
            additionalContent = thresholdEmail.AdditionalContent;

            var recipients = _emailSenderAndRecipients.SupportEmailAddresses ?? _emailSenderAndRecipients.SyncDisabledCCAddresses;

            if (!string.IsNullOrWhiteSpace(job.Requestor))
            {
                var recipientList = await GetThresholdRecipientsAsync(job.Requestor, job.TargetOfficeGroupId);
                if (recipientList.Count > 0)
                    recipients = string.Join(",", recipientList);
            }

            if (sendDisableJobNotification)
            {
                emailSubject = SyncThresholdDisablingJobEmailSubject;
                contentTemplate = SyncJobDisabledEmailBody;
                additionalContent = new[]
                {
                    groupName,
                    job.TargetOfficeGroupId.ToString(),
                    _gmmResources.LearnMoreAboutGMMUrl,
                    _emailSenderAndRecipients.SupportEmailAddresses
                };
            }

            await _graphUpdaterService.SendEmailAsync(
                    recipients,
                    contentTemplate,
                    additionalContent,
                    runId,
                    ccEmail: _emailSenderAndRecipients.SupportEmailAddresses,
                    emailSubject: emailSubject,
                    additionalSubjectParams: additionalSubjectContent);
        }

        private (string ContentTemplate, string[] AdditionalContent) GetThresholdEmail(string groupName, ThresholdResult threshold, SyncJob job)
        {
            string increasedThresholdMessage = null;
            string decreasedThresholdMessage = null;
            string contentTemplate = SyncThresholdBothEmailBody;
            string[] additionalContent;

            increasedThresholdMessage = _localizationRepository.TranslateSetting(
                                                        IncreaseThresholdMessage,
                                                        job.ThresholdPercentageForAdditions.ToString(),
                                                        threshold.IncreaseThresholdPercentage.ToString("F2"));

            decreasedThresholdMessage = _localizationRepository.TranslateSetting(
                                               DecreaseThresholdMessage,
                                               job.ThresholdPercentageForRemovals.ToString(),
                                               threshold.DecreaseThresholdPercentage.ToString("F2"));

            if (threshold.IsAdditionsThresholdExceeded && threshold.IsRemovalsThresholdExceeded)
            {
                additionalContent = new[]
                {
                      groupName,
                      job.TargetOfficeGroupId.ToString(),
                      $"{increasedThresholdMessage}\n{decreasedThresholdMessage}",
                      _gmmResources.LearnMoreAboutGMMUrl,
                      _emailSenderAndRecipients.SupportEmailAddresses
                };
            }
            else if (threshold.IsAdditionsThresholdExceeded)
            {
                additionalContent = new[]
                {
                      groupName,
                      job.TargetOfficeGroupId.ToString(),
                      $"{increasedThresholdMessage}\n",
                      _gmmResources.LearnMoreAboutGMMUrl,
                      _emailSenderAndRecipients.SupportEmailAddresses
                    };
            }
            else
            {
                additionalContent = new[]
                {
                      groupName,
                      job.TargetOfficeGroupId.ToString(),
                      $"{decreasedThresholdMessage}\n",
                      _gmmResources.LearnMoreAboutGMMUrl,
                      _emailSenderAndRecipients.SupportEmailAddresses
                };
            }

            return (contentTemplate, additionalContent);
        }

        private async Task<List<string>> GetThresholdRecipientsAsync(string requestors, Guid targetOfficeGroupId)
        {
            var recipients = new List<string>();
            var emails = requestors.Split(',', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

            foreach (var email in emails)
            {
                if (await _graphUpdaterService.IsEmailRecipientOwnerOfGroupAsync(email, targetOfficeGroupId))
                {
                    recipients.Add(email);
                }
            }

            if (recipients.Count > 0) return recipients;

            var top = _thresholdConfig.MaximumNumberOfThresholdRecipients > 0 ? _thresholdConfig.MaximumNumberOfThresholdRecipients + 1 : 0;
            var owners = await _graphUpdaterService.GetGroupOwnersAsync(targetOfficeGroupId, top);

            if (owners.Count <= _thresholdConfig.MaximumNumberOfThresholdRecipients || _thresholdConfig.MaximumNumberOfThresholdRecipients == 0)
            {
                recipients.AddRange(owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
            }

            return recipients;
        }

        private void SetupLoggingRepository(GroupMembership membership, SyncJob job)
        {
            _loggingRepository.DryRun = _isGraphUpdaterDryRunEnabled;
            if (job == null)
            {
                _loggingRepository.SyncJobProperties = new Dictionary<string, string>
                {
                    { nameof(SyncJob.PartitionKey), membership.SyncJobPartitionKey },
                    { nameof(SyncJob.RowKey), membership.SyncJobRowKey },
                    { nameof(SyncJob.TargetOfficeGroupId), membership.Destination.ObjectId.ToString() }
                };
            }
            else
                _loggingRepository.SyncJobProperties = job.ToDictionary();
        }
    }
}
