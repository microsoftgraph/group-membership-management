// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Models.Entities;
using Azure.Storage.Blobs.Models;

namespace Services
{
	public class JobTriggerService : IJobTriggerService
    {
        private const string EmailSubject = "EmailSubject";
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";
        private const string SyncDisabledNoOwnerEmailBody = "SyncDisabledNoOwnerEmailBody";
        private const int JobsBatchSize = 20;
        enum Metric
        {
            SyncJobsCount,
            TotalSyncJobsCount
        }

        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly INotificationTypesRepository _notificationTypesRepository;
        private readonly IJobNotificationsRepository _jobNotificationRepository;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly string _gmmAppId;
        private readonly IMailRepository _mailRepository;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly IGMMResources _gmmResources;
        private readonly IJobTriggerConfig _jobTriggerConfig;
        private readonly TelemetryClient _telemetryClient;

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
            INotificationTypesRepository notificationTypesRepository,
            IJobNotificationsRepository jobNotificationRepository,
            IServiceBusTopicsRepository serviceBusTopicsRepository,
            IGraphGroupRepository graphGroupRepository,
            IKeyVaultSecret<IJobTriggerService> gmmAppId,
            IMailRepository mailRepository,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGMMResources gmmResources,
            IJobTriggerConfig jobTriggerConfig,
            TelemetryClient telemetryClient
            )
        {
            _emailSenderAndRecipients = emailSenderAndRecipients;
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _jobNotificationRepository = jobNotificationRepository ?? throw new ArgumentNullException(nameof(jobNotificationRepository));
			_notificationTypesRepository = notificationTypesRepository ?? throw new ArgumentNullException(nameof(notificationTypesRepository));
			_serviceBusTopicsRepository = serviceBusTopicsRepository ?? throw new ArgumentNullException(nameof(serviceBusTopicsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _gmmAppId = gmmAppId.Secret;
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _jobTriggerConfig = jobTriggerConfig ?? throw new ArgumentNullException(nameof(jobTriggerConfig));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        public async Task<(List<SyncJob> jobs, bool jobTriggerThresholdExceeded)> GetSyncJobsAsync()
        {
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsAsync(false, SyncStatus.Idle, SyncStatus.InProgress, SyncStatus.StuckInProgress, SyncStatus.TransientError);
            var filteredJobs = ApplyJobTriggerFilters(jobs).ToList();
            var syncJobsCount = filteredJobs.Count;
            var totalSyncJobsCount = await _databaseSyncJobsRepository.GetSyncJobCountAsync(true, SyncStatus.All);
            _telemetryClient.TrackMetric(nameof(Metric.SyncJobsCount), syncJobsCount);
            _telemetryClient.TrackMetric(nameof(Metric.TotalSyncJobsCount), totalSyncJobsCount);
            var jobTriggerThresholdExceeded = HasJobTriggerThresholdExceeded(syncJobsCount, totalSyncJobsCount);
            return (filteredJobs, jobTriggerThresholdExceeded);
        }

        public async Task<string> GetGroupNameAsync(SyncJob job)
        {
            var destinationObjectId = (await ParseDestinationAsync(job)).ObjectId;
            return await _graphGroupRepository.GetGroupNameAsync(destinationObjectId);
        }

        public async Task SendEmailAsync(SyncJob job, string emailSubjectTemplateName, string emailContentTemplateName, string[] additionalContentParameters, string templateDirectory = "")
        {
            bool isNotificationDisabled = await IsNotificationDisabledAsync(job.Id, emailContentTemplateName);

            if (isNotificationDisabled) 
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = job.RunId,
                    Message = $"Notification template '{emailContentTemplateName}' is disabled for job {job.Id} with destination group {job.TargetOfficeGroupId}."
                });
                return;
            }
            string ownerEmails = null;
            string ccAddress = _emailSenderAndRecipients.SupportEmailAddresses;

            if (!SyncDisabledNoGroupEmailBody.Equals(emailContentTemplateName, StringComparison.InvariantCultureIgnoreCase))
            {
                var destinationObjectId = (await ParseDestinationAsync(job)).ObjectId;
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(destinationObjectId);
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

        public async Task<bool> IsNotificationDisabledAsync(Guid jobId, string notificationTypeName)
        {
            var notificationType = await _notificationTypesRepository.GetNotificationTypeByNotificationTypeNameAsync(notificationTypeName);

             if (notificationType == null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = jobId,
                    Message = $"No notification type ID found for notification type name '{notificationTypeName}'."
                });
                return false; 
            }

            if (notificationType.Disabled)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = jobId,
                    Message = $"Notifications of type '{notificationTypeName}' have been globally disabled."
                });
                return true;
            }

            return await _jobNotificationRepository.IsNotificationDisabledForJobAsync(jobId, notificationType.Id);
        }

        public async Task UpdateSyncJobAsync(SyncStatus? status, SyncJob job)
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
            var destinationObjectId = (await ParseDestinationAsync(job)).ObjectId;

            foreach (var strat in new JobVerificationStrategy[] {
                new JobVerificationStrategy { TestFunction = _graphGroupRepository.GroupExists, StatusMessage = $"Destination group {destinationObjectId} exists.", ErrorMessage = $"destination group {destinationObjectId} doesn't exist.", EmailBody = SyncDisabledNoGroupEmailBody },
                new JobVerificationStrategy { TestFunction = (groupId) => GMMCanWriteToGroupAsync(groupId), StatusMessage = $"GMM is an owner of destination group {destinationObjectId}.", ErrorMessage = $"GMM is not an owner of destination group {destinationObjectId}.", EmailBody = SyncDisabledNoOwnerEmailBody }})
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { RunId = job.RunId, Message = "Checking: " + strat.StatusMessage });
                // right now, we stop after the first failed strategy, because it doesn't make sense to find that the destination group doesn't exist and then check if we own it.
                // this can change in the future, when/if we have more than two things to check here.
                if (await strat.TestFunction(destinationObjectId) == false)
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
                        AdditionalContentParams = new[] { destinationObjectId.ToString(), _emailSenderAndRecipients.SupportEmailAddresses }
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

        public async Task<List<string>> GetGroupEndpointsAsync(SyncJob job)
        {
            var destinationObjectId = (await ParseDestinationAsync(job)).ObjectId;
            return await _graphGroupRepository.GetGroupEndpointsAsync(destinationObjectId);
        }

        public async Task<(bool IsValid, AzureADGroup DestinationObject)> ParseAndValidateDestinationAsync(SyncJob syncJob)
        {
            var destinationObject = await ParseDestinationAsync(syncJob);
            return destinationObject == null ? (false, null) : (true, destinationObject);
        }

        public async Task<AzureADGroup> ParseDestinationAsync(SyncJob syncJob)
        {
            if (string.IsNullOrWhiteSpace(syncJob.Destination)) return null;

            JObject destinationQuery = JArray.Parse(syncJob.Destination)[0] as JObject;
            Guid objectIdGuid;
            string type;

            if (destinationQuery["value"] == null ||
                destinationQuery["type"] == null ||
                destinationQuery["value"].SelectToken("objectId") == null ||
                !Guid.TryParse(destinationQuery["value"]["objectId"].Value<string>(), out objectIdGuid)) return null;

            type = destinationQuery["type"].Value<string>();

            if (type == "TeamsChannelMembership")
            {
                if (destinationQuery["value"].SelectToken("channelId") == null) return null;

                return new AzureADTeamsChannel
                {
                    Type = type,
                    ObjectId = objectIdGuid,
                    ChannelId = destinationQuery["value"]["channelId"].Value<string>()
                };
            }
            else if (type == "GroupMembership")
            {
                return new AzureADGroup
                {
                    Type = type,
                    ObjectId = objectIdGuid
                };
            }
            else { return null; }
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

        private bool HasJobTriggerThresholdExceeded(int syncJobsCount, int totalSyncJobsCount)
            {
                if (syncJobsCount < _jobTriggerConfig.JobCountThreshold)
                {
                    return false;
                }
                else if (syncJobsCount >= _jobTriggerConfig.JobCountThreshold)
                {
                    double percentage = ((double)syncJobsCount / totalSyncJobsCount) * 100;

                    if (percentage >= _jobTriggerConfig.JobPercentThreshold)
                    {
                        return true;
                    }
                }
                return false;
            }
    }
}
