// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using GraphUpdater.GraphUpdaterActivity;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GraphUpdaterApplication : IGraphUpdater
    {
        private const string EmailSubject = "EmailSubject";
        private const string SyncCompletedEmailBody = "SyncCompletedEmailBody";
        private const string SyncDisabledEmailBody = "SyncDisabledEmailBody";
        private const string SyncThresholdEmailBody = "SyncThresholdEmailBody";

        private readonly IMembershipDifferenceCalculator<AzureADUser> _differenceCalculator;
        private readonly IGraphGroupRepository _graphGroups;
        private readonly ISyncJobRepository _syncJobRepo;
        private readonly ILoggingRepository _log;
        private readonly IMailRepository _mailRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;

        public GraphUpdaterApplication(
            IMembershipDifferenceCalculator<AzureADUser> differenceCalculator,
            IGraphGroupRepository graphGroups,
            ISyncJobRepository syncJobRepository,
            ILoggingRepository logging,
            IMailRepository mailRepository,
            IGraphGroupRepository graphGroupRepository,
            IEmailSenderRecipient emailSenderAndRecipients
            )
        {
            _emailSenderAndRecipients = emailSenderAndRecipients;
            _differenceCalculator = differenceCalculator;
            _graphGroups = graphGroups;
            _syncJobRepo = syncJobRepository;
            _log = logging;
            _mailRepository = mailRepository;
            _graphGroupRepository = graphGroupRepository;
        }

        public async Task CalculateDifference(GroupMembership membership)
        {
            _graphGroups.RunId = membership.RunId;
            _log.SyncJobProperties = new Dictionary<string, string>
            {
                { "partitionKey", membership.SyncJobPartitionKey },
                { "rowKey", membership.SyncJobRowKey },
                { "targetOfficeGroupId", membership.Destination.ObjectId.ToString() }
            };
            var fromto = $"from {PrettyPrintSources(membership.Sources)} to {membership.Destination}";
            var changeTo = SyncStatus.Idle;

            await _log.LogMessageAsync(new LogMessage { Message = $"Processing sync job : Partition key {membership.SyncJobPartitionKey} , Row key {membership.SyncJobRowKey}", RunId = membership.RunId });

            var job = await _syncJobRepo.GetSyncJobAsync(membership.SyncJobPartitionKey, membership.SyncJobRowKey);
            if (job == null)
            {
                await _log.LogMessageAsync(new LogMessage { Message = $"Sync job : Partition key {membership.SyncJobPartitionKey}, Row key {membership.SyncJobRowKey} was not found!", RunId = membership.RunId });
                return;
            }

            await _log.LogMessageAsync(new LogMessage { Message = $"{job.TargetOfficeGroupId} job's status is {job.Status}.", RunId = membership.RunId });

            if (!(await _graphGroups.GroupExists(membership.Destination.ObjectId)))
            {
                await _log.LogMessageAsync(new LogMessage { Message = $"When syncing {fromto}, destination group {membership.Destination} doesn't exist. Not syncing and marking as error.", RunId = membership.RunId });
                changeTo = SyncStatus.Error;
            }

            if (changeTo == SyncStatus.Idle)
            {
                changeTo = await SynchronizeGroups(job, membership, fromto);
            }

            await _log.LogMessageAsync(new LogMessage { Message = $"Set job status to {changeTo}.", RunId = membership.RunId });

            job.LastRunTime = DateTime.UtcNow;
            job.RunId = membership.RunId;
            job.Enabled = changeTo != SyncStatus.Error;
            await _syncJobRepo.UpdateSyncJobStatusAsync(new[] { job }, changeTo);

            await _log.LogMessageAsync(new LogMessage { Message = $"Syncing {fromto} done.", RunId = membership.RunId });
        }

        private async Task<SyncStatus> SynchronizeGroups(SyncJob job, GroupMembership membership, string fromto)
        {
            if (membership.Errored)
            {
                await _log.LogMessageAsync(new LogMessage { Message = $"When syncing {fromto}, calculator reported an error. Not syncing and marking as error.", RunId = membership.RunId });
                await SendEmailAsync(job.Requestor, SyncDisabledEmailBody, new[] { PrettyPrintSources(membership.Sources) }, _emailSenderAndRecipients.SyncDisabledCCAddresses);
                return SyncStatus.Error;
            }

            var isInitialSync = job.LastRunTime == DateTime.FromFileTimeUtc(0);
            var delta = await CalculateDeltaAsync(membership, fromto);
            var threshold = await CalculateThresholdAsync(job, delta.Delta, delta.TotalMembersCount, membership.RunId);

            if (isInitialSync || !threshold.IsThresholdExceeded)
            {
                await DoSynchronization(delta.Delta, membership, fromto);

                if (isInitialSync)
                {
                    var groupName = await _graphGroupRepository.GetGroupNameAsync(job.TargetOfficeGroupId);
                    var additonalContent = new[] { groupName, job.TargetOfficeGroupId.ToString(), delta.Delta.ToAdd.Count.ToString(), delta.Delta.ToRemove.Count.ToString() };
                    await SendEmailAsync(job.Requestor, SyncCompletedEmailBody, additonalContent, _emailSenderAndRecipients.SyncCompletedCCAddresses);
                }
            }
            else
            {
                var groupName = await _graphGroupRepository.GetGroupNameAsync(job.TargetOfficeGroupId);
                var additonalContent = new[]
                { groupName, job.TargetOfficeGroupId.ToString(),
                  job.ThresholdPercentageForAdditions.ToString(), threshold.IncreaseThresholdPercentage.ToString(),
                  job.ThresholdPercentageForRemovals.ToString(),  threshold.DecreaseThresholdPercentage.ToString()
                };

                await SendEmailAsync(_emailSenderAndRecipients.SyncDisabledCCAddresses, SyncThresholdEmailBody, additonalContent);
            }

            return SyncStatus.Idle;
        }

        private async Task DoSynchronization(MembershipDelta<AzureADUser> delta, GroupMembership membership, string fromto)
        {
            var stopwatch = Stopwatch.StartNew();
            await _graphGroups.AddUsersToGroup(delta.ToAdd, membership.Destination);
            await _graphGroups.RemoveUsersFromGroup(delta.ToRemove, membership.Destination);
            stopwatch.Stop();

            await _log.LogMessageAsync(new LogMessage { Message = $"Synchronization {fromto} complete in {stopwatch.Elapsed.TotalSeconds} seconds. {delta.ToAdd.Count / stopwatch.Elapsed.TotalSeconds} users added per second. {delta.ToRemove.Count / stopwatch.Elapsed.TotalSeconds} users removed per second. Marking job as idle.", RunId = membership.RunId });
        }

        private async Task<(MembershipDelta<AzureADUser> Delta, int TotalMembersCount)> CalculateDeltaAsync(GroupMembership membership, string fromto)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"Calculating membership difference {fromto}.", RunId = membership.RunId });

            var stopwatch = Stopwatch.StartNew();
            var destinationMembers = await _graphGroups.GetUsersInGroupTransitively(membership.Destination.ObjectId);
            var delta = _differenceCalculator.CalculateDifference(membership.SourceMembers, destinationMembers);

            stopwatch.Stop();
            await _log.LogMessageAsync(new LogMessage { Message = $"Calculated membership difference {fromto} in {stopwatch.Elapsed.TotalSeconds} seconds. Adding {delta.ToAdd.Count} users and removing {delta.ToRemove.Count}.", RunId = membership.RunId });

            return (delta, destinationMembers.Count);
        }

        private async Task<ThresholdResult> CalculateThresholdAsync(SyncJob job, MembershipDelta<AzureADUser> delta, int totalMembersCount, Guid runId)
        {
            double percentageIncrease = 0;
            double percentageDecrease = 0;
            bool isAdditionsThresholdExceeded = false;
            bool isRemovalsThresholdExceeded = false;

            if (job.ThresholdPercentageForAdditions > 0)
            {
                percentageIncrease = delta.ToAdd.Count / totalMembersCount * 100;
                isAdditionsThresholdExceeded = percentageIncrease > job.ThresholdPercentageForAdditions;

                if (isAdditionsThresholdExceeded)
                {
                    await _log.LogMessageAsync(new LogMessage { Message = $"Membership increase in {job.TargetOfficeGroupId} is {percentageIncrease}% and is greater than threshold value {job.ThresholdPercentageForAdditions}%", RunId = runId });
                }
            }

            if (job.ThresholdPercentageForRemovals > 0)
            {
                percentageDecrease = delta.ToRemove.Count / totalMembersCount * 100;
                isRemovalsThresholdExceeded = percentageDecrease > job.ThresholdPercentageForRemovals;

                if (isRemovalsThresholdExceeded)
                {
                    await _log.LogMessageAsync(new LogMessage { Message = $"Membership decrease in {job.TargetOfficeGroupId} is {percentageDecrease}% and is lesser than threshold value {job.ThresholdPercentageForRemovals}%", RunId = runId });
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

        private async Task SendEmailAsync(string toEmail, string contentTemplate, string[] additionalContent, string ccEmailAddresses = null)
        {
            var message = new EmailMessage
            {
                Subject = EmailSubject,
                Content = contentTemplate,
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = toEmail,
                CcEmailAddresses = ccEmailAddresses,
                AdditionalContentParams = additionalContent
            };

            await _mailRepository.SendMailAsync(message);
        }

        private string PrettyPrintSources(AzureADGroup[] sources)
        {
            if (sources == null || sources.Length == 0)
                return "a non-security group sync";
            return string.Join(',', sources.AsEnumerable());
        }
    }

    public interface IGraphUpdater
    {
        Task CalculateDifference(GroupMembership groupMembership);
    }
}
