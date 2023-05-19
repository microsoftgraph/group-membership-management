// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.ServiceBus;
using Models;
using Models.ThresholdNotifications;
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
        private const string IncreaseThresholdMessage = "IncreaseThresholdMessage";
        private const string DecreaseThresholdMessage = "DecreaseThresholdMessage";
        private const string SyncJobDisabledEmailBody = "SyncJobDisabledEmailBody";
        private const string SyncThresholdEmailSubject = "SyncThresholdEmailSubject";
        private const string SyncThresholdBothEmailBody = "SyncThresholdBothEmailBody";
        private const string SyncThresholdDisablingJobEmailSubject = "SyncThresholdDisablingJobEmailSubject";

        private readonly ISyncJobRepository _syncJobRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly IGraphAPIService _graphAPIService;
        private readonly IThresholdConfig _thresholdConfig;
        private readonly IGMMResources _gmmResources;
        private readonly ILocalizationRepository _localizationRepository;
        private readonly INotificationRepository _notificationRepository;
        private readonly bool _isDryRunEnabled;
        private readonly IThresholdNotificationConfig _thresholdNotificationConfig;

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
            ISyncJobRepository syncJobRepository,
            ILoggingRepository loggingRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGraphAPIService graphAPIService,
            IDryRunValue dryRun,
            IThresholdConfig thresholdConfig,
            IThresholdNotificationConfig thresholdNotificationConfig,
            IGMMResources gmmResources,
            ILocalizationRepository localizationRepository,
            INotificationRepository notificationRepository
            )
        {
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService));
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
            _thresholdNotificationConfig = thresholdNotificationConfig ?? throw new ArgumentNullException(nameof(thresholdNotificationConfig));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _isDryRunEnabled = dryRun != null && dryRun.DryRunEnabled;
        }

        public async Task<DeltaResponse> CalculateDifferenceAsync(GroupMembership sourceMembership, GroupMembership destinationMembership)
        {
            var deltaResponse = new DeltaResponse
            {
                MembershipDeltaStatus = MembershipDeltaStatus.Ok
            };

            var job = await _syncJobRepository.GetSyncJobAsync(sourceMembership.SyncJobPartitionKey, sourceMembership.SyncJobRowKey);
            if (job == null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sync job : Partition key {sourceMembership.SyncJobPartitionKey}, Row key {sourceMembership.SyncJobRowKey} was not found!", RunId = sourceMembership.RunId });
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
                Message = $"Processing sync job : Partition key {sourceMembership.SyncJobPartitionKey} , Row key {sourceMembership.SyncJobRowKey}",
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
                var isInitialSync = job.LastRunTime == DateTime.FromFileTimeUtc(0);
                var threshold = isInitialSync ? new ThresholdResult() : await CalculateThresholdAsync(job, delta.Delta, delta.TotalMembersCount, sourceMembership.RunId);

                deltaResponse.MembersToAdd = delta.Delta.ToAdd;
                deltaResponse.MembersToRemove = delta.Delta.ToRemove;

                if (threshold.IsThresholdExceeded)
                {
                    deltaResponse.MembershipDeltaStatus = job.IgnoreThresholdOnce ? MembershipDeltaStatus.Ok : MembershipDeltaStatus.ThresholdExceeded;

                    if (job.IgnoreThresholdOnce)
                        await LogIgnoreThresholdOnceAsync(job, sourceMembership.RunId);
                    else
                    {
                        if(_thresholdNotificationConfig.IsThresholdNotificationEnabled)
                        {
                            var thresholdNotification = new Models.ThresholdNotifications.ThresholdNotification { 
                                Id = Guid.NewGuid(),
                                ChangePercentageForAdditions = (int)threshold.IncreaseThresholdPercentage,
                                ChangePercentageForRemovals = (int)threshold.DecreaseThresholdPercentage,
                                ChangeQuantityForAdditions = delta.Delta.ToAdd.Count,
                                ChangeQuantityForRemovals = delta.Delta.ToRemove.Count,
                                CreatedTime = DateTime.UtcNow,
                                Resolution = ThresholdNotificationResolution.Unresolved,
                                ResolvedByUPN = string.Empty,
                                ResolvedTime = DateTime.FromFileTimeUtc(0),
                                Status = ThresholdNotificationStatus.Queued,
                                TargetOfficeGroupId = job.TargetOfficeGroupId,
                                ThresholdPercentageForAdditions = job.ThresholdPercentageForAdditions,
                                ThresholdPercentageForRemovals = job.ThresholdPercentageForRemovals
                            };

                            await _notificationRepository.SaveNotificationAsync(thresholdNotification);
                        }
                        else
                        {
                            await SendThresholdNotificationAsync(threshold, job, sourceMembership.RunId);
                        }
                    }

                    return deltaResponse;
                }

                if (isDryRunSync)
                {
                    deltaResponse.MembershipDeltaStatus = MembershipDeltaStatus.DryRun;
                    return deltaResponse;
                }
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

        private async Task SendThresholdNotificationAsync(ThresholdResult threshold, SyncJob job, Guid runId)
        {
            var currentThresholdViolations = job.ThresholdViolations + 1;
            var followUpLimit = _thresholdConfig.NumberOfThresholdViolationsToNotify + _thresholdConfig.NumberOfThresholdViolationsFollowUps;
            var sendNotification = currentThresholdViolations >= _thresholdConfig.NumberOfThresholdViolationsToNotify && currentThresholdViolations <= followUpLimit;
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

            var emailSubject = SyncThresholdEmailSubject;

            string contentTemplate;
            string[] additionalContent;
            string[] additionalSubjectContent = new[] { job.TargetOfficeGroupId.ToString(), groupName };

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
                    job.TargetOfficeGroupId.ToString(),
                    groupName,
                    _gmmResources.LearnMoreAboutGMMUrl,
                    _emailSenderAndRecipients.SupportEmailAddresses
                };
            }

            await _graphAPIService.SendEmailAsync(
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
            string increasedThresholdMessage;
            string decreasedThresholdMessage;
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
                      job.TargetOfficeGroupId.ToString(),
                      groupName,
                      $"{increasedThresholdMessage}\n{decreasedThresholdMessage}",
                      _gmmResources.LearnMoreAboutGMMUrl,
                      _emailSenderAndRecipients.SupportEmailAddresses
                };
            }
            else if (threshold.IsAdditionsThresholdExceeded)
            {
                additionalContent = new[]
                {
                      job.TargetOfficeGroupId.ToString(), 
                      groupName,
                      $"{increasedThresholdMessage}\n",
                      _gmmResources.LearnMoreAboutGMMUrl,
                      _emailSenderAndRecipients.SupportEmailAddresses
                    };
            }
            else
            {
                additionalContent = new[]
                {
                      job.TargetOfficeGroupId.ToString(),
                      groupName,
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
                if (await _graphAPIService.IsEmailRecipientOwnerOfGroupAsync(email, targetOfficeGroupId))
                {
                    recipients.Add(email);
                }
            }

            if (recipients.Count > 0) return recipients;

            var top = _thresholdConfig.MaximumNumberOfThresholdRecipients > 0 ? _thresholdConfig.MaximumNumberOfThresholdRecipients + 1 : 0;
            var owners = await _graphAPIService.GetGroupOwnersAsync(targetOfficeGroupId, top);

            if (owners.Count <= _thresholdConfig.MaximumNumberOfThresholdRecipients || _thresholdConfig.MaximumNumberOfThresholdRecipients == 0)
            {
                recipients.AddRange(owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
            }

            return recipients;
        }
    }
}
