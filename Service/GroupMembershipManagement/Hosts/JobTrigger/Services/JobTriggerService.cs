// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
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

        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobRepository _syncJobRepository;
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
            ISyncJobRepository syncJobRepository,
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
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _serviceBusTopicsRepository = serviceBusTopicsRepository ?? throw new ArgumentNullException(nameof(serviceBusTopicsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _gmmAppId = gmmAppId.Secret;
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _jobTriggerConfig = jobTriggerConfig ?? throw new ArgumentNullException(nameof(jobTriggerConfig));
        }

        public async Task<List<SyncJob>> GetSyncJobsAsync()
        {
            var allJobs = new List<SyncJob>();
            var jobs = _syncJobRepository.GetSyncJobsAsync(SyncStatus.Idle);
            await foreach (var job in jobs)
            {
                allJobs.Add(job);
            }
            return allJobs;
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task SendEmailAsync(SyncJob job, string emailTemplateName, string[] additionalContentParameters)
        {
            string ownerEmails = null;
            string ccAddress = _emailSenderAndRecipients.SupportEmailAddresses;

            if (!SyncDisabledNoGroupEmailBody.Equals(emailTemplateName, StringComparison.InvariantCultureIgnoreCase))
            {
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(job.TargetOfficeGroupId);
                ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
            }

            if (emailTemplateName.Contains("disabled", StringComparison.InvariantCultureIgnoreCase))
                ccAddress = _emailSenderAndRecipients.SyncDisabledCCAddresses;

            var message = new EmailMessage
            {
                Subject = EmailSubject,
                Content = emailTemplateName,
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = ownerEmails ?? job.Requestor,
                CcEmailAddresses = ccAddress,
                AdditionalContentParams = additionalContentParameters
            };

            await _mailRepository.SendMailAsync(message, job.RunId);
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
            }

            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { job }, status);
        }

        public async Task SendMessageAsync(SyncJob job)
        {
            await _serviceBusTopicsRepository.AddMessageAsync(job);
        }

        public async Task<bool> GroupExistsAndGMMCanWriteToGroupAsync(SyncJob job)
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
                    }, job.RunId);
                    return false;
                }

                await _loggingRepository.LogMessageAsync(new LogMessage { RunId = job.RunId, Message = "Check passed: " + strat.StatusMessage });
            }

            return true;
        }

        private async Task<bool> GMMCanWriteToGroupAsync(Guid groupId)
        {
            if(_jobTriggerConfig.GMMHasGroupReadWriteAllPermissions)
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
    }
}
