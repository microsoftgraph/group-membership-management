// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class AzureMaintenanceService : IAzureMaintenanceService
	{
        private const string CustomerPausedJobEmailSubject = "CustomerPausedJobEmailSubject";
        private const string CustomerPausedJobEmailBody = "CustomerPausedJobEmailBody";

        private readonly IDatabaseSyncJobsRepository _syncJobRepository = null;
        private readonly IDatabasePurgedSyncJobsRepository _purgedSyncJobRepository = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
        private readonly IMailRepository _mailRepository = null;
        private readonly IHandleInactiveJobsConfig _handleInactiveJobsConfig = null;

        public AzureMaintenanceService(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabasePurgedSyncJobsRepository purgedSyncJobRepository,
            IGraphGroupRepository graphGroupRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            IMailRepository mailRepository,
            IHandleInactiveJobsConfig handleInactiveJobsConfig)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _purgedSyncJobRepository = purgedSyncJobRepository ?? throw new ArgumentNullException(nameof(purgedSyncJobRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _handleInactiveJobsConfig = handleInactiveJobsConfig ?? throw new ArgumentNullException(nameof(handleInactiveJobsConfig));
        }

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            var allJobs = new List<SyncJob>();
            var jobs = await _syncJobRepository.GetSyncJobsAsync(false,
                SyncStatus.CustomerPaused,
                SyncStatus.DestinationGroupNotFound,
                SyncStatus.MembershipDataNotFound,
                SyncStatus.NotOwnerOfDestinationGroup,
                SyncStatus.SecurityGroupNotFound,
                SyncStatus.ThresholdExceeded);
            if (jobs == null) { return allJobs; }

            return ApplyJobTriggerFilters(jobs).ToList();
        }

        private IEnumerable<SyncJob> ApplyJobTriggerFilters(IEnumerable<SyncJob> jobs)
        {
            return jobs.Where(x => ((DateTime.UtcNow - x.LastRunTime) > TimeSpan.FromDays(30)));
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task SendEmailAsync(SyncJob job, string groupName)
        {
            if (job != null)
            {
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(job.TargetOfficeGroupId);
                var ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
                var message = new EmailMessage
                {
                    Subject = CustomerPausedJobEmailSubject,
                    Content = CustomerPausedJobEmailBody,
                    SenderAddress = _emailSenderAndRecipients.SenderAddress,
                    SenderPassword = _emailSenderAndRecipients.SenderPassword,
                    ToEmailAddresses = ownerEmails,
                    CcEmailAddresses = _emailSenderAndRecipients.SupportEmailAddresses,
                    AdditionalContentParams = new[]
                    {
                        groupName,
                        job.TargetOfficeGroupId.ToString(),
                        DateTime.UtcNow.AddDays(_handleInactiveJobsConfig.NumberOfDaysBeforeDeletion-5).ToString()
                    }
                };

                await _mailRepository.SendMailAsync(message, job.RunId);
            }
        }

        public async Task<int> BackupInactiveJobsAsync(List<SyncJob> syncJobs)
        {
            if (syncJobs.Count <= 0) return 0;
            var purgedJobs = MapSyncJobsToPurgedSyncJobs(syncJobs);
            var a = await _purgedSyncJobRepository.InsertPurgedSyncJobsAsync(purgedJobs);
            return a;
        }

        private List<PurgedSyncJob> MapSyncJobsToPurgedSyncJobs(List<SyncJob> syncJobs)
        {
            return syncJobs.Select(x => MapSyncJobToPurgedSyncJob(x)).ToList();
        }

        private PurgedSyncJob MapSyncJobToPurgedSyncJob(SyncJob job)
        {
            return new PurgedSyncJob()
            {
                Id = new Guid(),
                IgnoreThresholdOnce = job.IgnoreThresholdOnce,
                IsDryRunEnabled = job.IsDryRunEnabled,
                DryRunTimeStamp = job.DryRunTimeStamp,
                LastRunTime = job.LastRunTime,
                LastSuccessfulRunTime = job.LastSuccessfulRunTime,
                LastSuccessfulStartTime = job.LastSuccessfulStartTime,
                StartDate = job.StartDate,
                Timestamp = job.Timestamp,
                TargetOfficeGroupId = job.TargetOfficeGroupId,
                Destination = job.Destination,
                AllowEmptyDestination = job.AllowEmptyDestination,
                RunId = job.RunId,
                Period = job.Period,
                ThresholdPercentageForAdditions = job.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = job.ThresholdPercentageForRemovals,
                ThresholdViolations = job.ThresholdViolations,
                Query = job.Query,
                Requestor = job.Requestor,
                Status = job.Status,
                PurgedAt = DateTime.UtcNow
            };
        }

        public async Task<int> RemoveBackupsAsync()
        {
            var cutOffDate = DateTime.UtcNow.AddDays(-_handleInactiveJobsConfig.NumberOfDaysBeforeDeletion);
            var jobs = await _purgedSyncJobRepository.GetPurgedSyncJobsAsync(cutOffDate);
            if (jobs.ToList().Count <= 0) return 0;
            return await _purgedSyncJobRepository.DeletePurgedSyncJobsAsync(jobs);
        }

        public async Task RemoveInactiveJobsAsync(IEnumerable<SyncJob> jobs)
        {
            await _syncJobRepository.DeleteSyncJobsAsync(jobs);
        }
    }
}