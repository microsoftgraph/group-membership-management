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
    public class JobTriggerService : IJobTriggerService
    {
        private const string EmailSubject = "EmailSubject";
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";
        private const string SyncDisabledNoOwnerEmailBody = "SyncDisabledNoOwnerEmailBody";
        private const int JobsBatchSize = 20;

        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly string _gmmAppId;
        private readonly IMailRepository _mailRepository;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly IGMMResources _gmmResources;
        private readonly IJobTriggerConfig _jobTriggerConfig;

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
            IServiceBusTopicsRepository serviceBusTopicsRepository,
            IGraphGroupRepository graphGroupRepository,
            IKeyVaultSecret<IJobTriggerService> gmmAppId,
            IMailRepository mailRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGMMResources gmmResources,
            IJobTriggerConfig jobTriggerConfig
            )
        {
            _emailSenderAndRecipients = emailSenderAndRecipients;
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _serviceBusTopicsRepository = serviceBusTopicsRepository ?? throw new ArgumentNullException(nameof(serviceBusTopicsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _gmmAppId = gmmAppId.Secret;
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _jobTriggerConfig = jobTriggerConfig ?? throw new ArgumentNullException(nameof(jobTriggerConfig));
        }

        public async Task<List<SyncJob>> GetSyncJobsSegmentAsync()
        {
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsAsync(false, SyncStatus.Idle, SyncStatus.InProgress, SyncStatus.StuckInProgress);
            return ApplyJobTriggerFilters(jobs).ToList();
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task SendEmailAsync(SyncJob job, string emailSubjectTemplateName, string emailContentTemplateName, string[] additionalContentParameters, string templateDirectory = "")
        {
            string ownerEmails = null;
            string ccAddress = _emailSenderAndRecipients.SupportEmailAddresses;

            if (!SyncDisabledNoGroupEmailBody.Equals(emailContentTemplateName, StringComparison.InvariantCultureIgnoreCase))
            {
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(job.TargetOfficeGroupId);
                ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
            }

            if (emailContentTemplateName.Contains("disabled", StringComparison.InvariantCultureIgnoreCase))
                ccAddress = _emailSenderAndRecipients.SyncDisabledCCAddresses;

            var message = new EmailMessage
            {
                Subject = emailSubjectTemplateName ?? EmailSubject,
                Content = emailContentTemplateName,
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = ownerEmails ?? job.Requestor,
                CcEmailAddresses = ccAddress,
                AdditionalContentParams = additionalContentParameters
            };

            await _mailRepository.SendMailAsync(message, job.RunId, templateDirectory);
        }

        public async Task UpdateSyncJobStatusAsync(SyncStatus status, SyncJob job)
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

        public async Task<bool> GroupExistsAndGMMCanWriteToGroupAsync(SyncJob job, string templateDirectory = "")
        {
            foreach (var strat in new JobVerificationStrategy[] {
                new JobVerificationStrategy { TestFunction = _graphGroupRepository.GroupExists, StatusMessage = $"Destination group {job.TargetOfficeGroupId} exists.", ErrorMessage = $"destination group {job.TargetOfficeGroupId} doesn't exist.", EmailBody = SyncDisabledNoGroupEmailBody },
                new JobVerificationStrategy { TestFunction = (groupId) => GMMCanWriteToGroupAsync(groupId), StatusMessage = $"GMM is an owner of destination group {job.TargetOfficeGroupId}.", ErrorMessage = $"GMM is not an owner of destination group {job.TargetOfficeGroupId}.", EmailBody = SyncDisabledNoOwnerEmailBody }})
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { RunId = job.RunId, Message = "Checking: " + strat.StatusMessage });
                // right now, we stop after the first failed strategy, because it doesn't make sense to find that the destination group doesn't exist and then check if we own it.
                // this can change in the future, when/if we have more than two things to check here.
                if (await strat.TestFunction(job.TargetOfficeGroupId) == false)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { RunId = job.RunId, Message = "Marking sync job as failed because " + strat.ErrorMessage });
                    await _mailRepository.SendMailAsync(new EmailMessage
                    {
                        Subject = EmailSubject,
                        Content = strat.EmailBody,
                        SenderAddress = _emailSenderAndRecipients.SenderAddress,
                        SenderPassword = _emailSenderAndRecipients.SenderPassword,
                        ToEmailAddresses = job.Requestor,
                        CcEmailAddresses = _emailSenderAndRecipients.SyncDisabledCCAddresses,
                        AdditionalContentParams = new[] { job.TargetOfficeGroupId.ToString(), _emailSenderAndRecipients.SupportEmailAddresses }
                    }, job.RunId, templateDirectory);
                    return false;
                }

                await _loggingRepository.LogMessageAsync(new LogMessage { RunId = job.RunId, Message = "Check passed: " + strat.StatusMessage });
            }

            return true;
        }

        private async Task<bool> GMMCanWriteToGroupAsync(Guid groupId)
        {
            if (_jobTriggerConfig.GMMHasGroupReadWriteAllPermissions)
                return true;

            var isAppIdOwner = await _graphGroupRepository.IsAppIDOwnerOfGroup(_gmmAppId, groupId);
            return isAppIdOwner;
        }

        public async Task<List<string>> GetGroupEndpointsAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupEndpointsAsync(groupId);
        }

        private class JobVerificationStrategy
        {
            public delegate Task<bool> CanWriteToGroup(Guid groupId);

            public CanWriteToGroup TestFunction { get; set; }
            public string StatusMessage { get; set; }
            public string ErrorMessage { get; set; }
            public string EmailBody { get; set; }
        }

        private IEnumerable<SyncJob> ApplyJobTriggerFilters(IEnumerable<SyncJob> jobs)
        {
            var allNonDryRunSyncJobs = jobs.Where(x => ((DateTime.UtcNow - x.LastRunTime) > TimeSpan.FromHours(x.Period)) && x.IsDryRunEnabled == false && x.Status != SyncStatus.InProgress.ToString());
            var allDryRunSyncJobs = jobs.Where(x => ((DateTime.UtcNow - x.DryRunTimeStamp) > TimeSpan.FromHours(x.Period)) && x.IsDryRunEnabled == true && x.Status != SyncStatus.InProgress.ToString());
            var inProgressSyncJobs = jobs.Where(x => ((DateTime.UtcNow - x.LastSuccessfulStartTime) > TimeSpan.FromHours(x.Period)) && x.Status == SyncStatus.InProgress.ToString());
            return allNonDryRunSyncJobs.Concat(allDryRunSyncJobs).Concat(inProgressSyncJobs);
        }
    }
}
