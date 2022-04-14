// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
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
        private const string SyncStartedEmailBody = "SyncStartedEmailBody";
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

        public JobTriggerService(
            ILoggingRepository loggingRepository,
            ISyncJobRepository syncJobRepository,
            IServiceBusTopicsRepository serviceBusTopicsRepository,
            IGraphGroupRepository graphGroupRepository,
            IKeyVaultSecret<IJobTriggerService> gmmAppId,
            IMailRepository mailRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGMMResources gmmResources
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

        public async Task SendEmailAsync(SyncJob job, string groupName)
        {
            if (job != null && job.LastRunTime == DateTime.FromFileTimeUtc(0))
            {
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(job.TargetOfficeGroupId);
                var ownerEmails = string.Join(";", owners.Where(x => !string.IsNullOrWhiteSpace(x.Mail)).Select(x => x.Mail));
                var message = new EmailMessage
                {
                    Subject = EmailSubject,
                    Content = SyncStartedEmailBody,
                    SenderAddress = _emailSenderAndRecipients.SenderAddress,
                    SenderPassword = _emailSenderAndRecipients.SenderPassword,
                    ToEmailAddresses = ownerEmails,
                    CcEmailAddresses = string.Empty,
                    AdditionalContentParams = new[]
                    {
                        groupName,
                        job.TargetOfficeGroupId.ToString(),
                        _emailSenderAndRecipients.SupportEmailAddresses,
                        _gmmResources.LearnMoreAboutGMMUrl,
                        job.Requestor
                    }
                };

                await _mailRepository.SendMailAsync(message, job.RunId);
            }
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

            // Don't leak this to the start and stop logs.
            // The logging repository has this SyncJobInfo property that gets appended to all the logs,
            // to make it easier to log information like the run ID and so on without having to pass all that around.
            // However, the same logging repository gets reused for the life of the program, which means that, without this line,
            // it'll append that information to the logs that say "JobTrigger function started" and "JobTrigger function completed".

            _loggingRepository.SyncJobProperties = null;
        }

        public async Task SendMessageAsync(SyncJob job)
        {
            await _serviceBusTopicsRepository.AddMessageAsync(job);
        }

        public async Task<bool> CanWriteToGroup(SyncJob job)
        {
            foreach (var strat in new JobVerificationStrategy[] {
                new JobVerificationStrategy { TestFunction = _graphGroupRepository.GroupExists, StatusMessage = $"Destination group {job.TargetOfficeGroupId} exists.", ErrorMessage = $"destination group {job.TargetOfficeGroupId} doesn't exist.", EmailBody = SyncDisabledNoGroupEmailBody },
                new JobVerificationStrategy { TestFunction = (groupId) => _graphGroupRepository.IsAppIDOwnerOfGroup(_gmmAppId, groupId), StatusMessage = $"GMM is an owner of destination group {job.TargetOfficeGroupId}.", ErrorMessage = $"GMM is not an owner of destination group {job.TargetOfficeGroupId}.", EmailBody = SyncDisabledNoOwnerEmailBody }})
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
