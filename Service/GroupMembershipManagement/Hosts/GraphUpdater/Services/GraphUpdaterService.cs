// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Models;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using Metric = Services.Entities.Metric;

namespace Services
{
    public class GraphUpdaterService : IGraphUpdaterService
    {
        private const int NumberOfGraphRetries = 5;
        private const string EmailSubject = "EmailSubject";
        private readonly ILoggingRepository _loggingRepository;
        private readonly TelemetryClient _telemetryClient;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IMailRepository _mailRepository;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly ISyncJobRepository _syncJobRepository;

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

        public GraphUpdaterService(
                ILoggingRepository loggingRepository,
                TelemetryClient telemetryClient,
                IGraphGroupRepository graphGroupRepository,
                IMailRepository mailRepository,
                IEmailSenderRecipient emailSenderAndRecipients,
                ISyncJobRepository syncJobRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
        }

        public async Task<UsersPageResponse> GetFirstMembersPageAsync(Guid groupId, Guid runId)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Reading users from the group with ID {groupId}." });
            _graphGroupRepository.RunId = runId;
            var result = await _graphGroupRepository.GetFirstTransitiveMembersPageAsync(groupId);
            return new UsersPageResponse
            {
                NextPageUrl = result.nextPageUrl,
                Members = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                MembersPage = result.usersFromGroup
            };
        }

        public async Task<UsersPageResponse> GetNextMembersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup, Guid runId)
        {
            _graphGroupRepository.RunId = runId;
            var result = await _graphGroupRepository.GetNextTransitiveMembersPageAsync(nextPageUrl, usersFromGroup);
            return new UsersPageResponse
            {
                NextPageUrl = result.nextPageUrl,
                Members = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                MembersPage = result.usersFromGroup
            };
        }

        public async Task<PolicyResult<bool>> GroupExistsAsync(Guid groupId, Guid runId)
        {
            var graphRetryPolicy = Policy.Handle<SocketException>()
                                    .WaitAndRetryAsync(NumberOfGraphRetries, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                   onRetry: async (ex, count) =>
                   {
                       await _loggingRepository.LogMessageAsync(new LogMessage
                       {
                           Message = $"Got a transient SocketException. Retrying. This was try {count} out of {NumberOfGraphRetries}.\n" + ex.ToString(),
                           RunId = runId
                       });
                   });

            return await graphRetryPolicy.ExecuteAndCaptureAsync(() => _graphGroupRepository.GroupExists(groupId));
        }

        public async Task SendEmailAsync(string toEmail, string contentTemplate, string[] additionalContentParams, Guid runId, string ccEmail = null, string emailSubject = null, string[] additionalSubjectParams = null, string adaptiveCardTemplateDirectory = "")
        {
            await _mailRepository.SendMailAsync(new EmailMessage
            {
                Subject = emailSubject ?? EmailSubject,
                Content = contentTemplate,
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = toEmail,
                CcEmailAddresses = ccEmail,
                AdditionalContentParams = additionalContentParams,
                AdditionalSubjectParams = additionalSubjectParams
            }, runId, adaptiveCardTemplateDirectory);
        }

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status, bool isDryRun, Guid runId)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Set job status to {status}.", RunId = runId });

            var isDryRunSync = job.IsDryRunEnabled || isDryRun;

            if (isDryRunSync)
                job.DryRunTimeStamp = DateTime.UtcNow;
            else
            {
                if (status == SyncStatus.Idle)
                    job.LastSuccessfulRunTime = DateTime.UtcNow;

                job.LastRunTime = DateTime.UtcNow;
            }

            job.RunId = runId;

            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { job }, status);

            string message = isDryRunSync
                                ? $"Dry Run of a sync to {job.TargetOfficeGroupId} is complete. Membership will not be updated."
                                : $"Syncing to {job.TargetOfficeGroupId} done.";

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = message, RunId = runId });
        }

        public async Task<SyncJob> GetSyncJobAsync(string partitionKey, string rowKey)
        {
            return await _syncJobRepository.GetSyncJobAsync(partitionKey, rowKey);
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound)> AddUsersToGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync)
        {
            var stopwatch = Stopwatch.StartNew();
            var graphResponse = await _graphGroupRepository.AddUsersToGroup(members, new AzureADGroup { ObjectId = targetGroupId });
            stopwatch.Stop();

            if (isInitialSync)
                _telemetryClient.TrackMetric(nameof(Metric.MembersAddedFromOnboarding), graphResponse.SuccessCount);
            else
                _telemetryClient.TrackMetric(nameof(Metric.MembersAdded), graphResponse.SuccessCount);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Adding {members.Count} users to group {targetGroupId} complete in {stopwatch.Elapsed.TotalSeconds} seconds. " +
                $"{members.Count / stopwatch.Elapsed.TotalSeconds} users added per second. ",
                RunId = runId,
            }, VerbosityLevel.DEBUG);
            _telemetryClient.TrackMetric(nameof(Metric.GraphAddRatePerSecond), members.Count / stopwatch.Elapsed.TotalSeconds);

            var status = graphResponse.ResponseCode == ResponseCode.Error ? GraphUpdaterStatus.Error : GraphUpdaterStatus.Ok;
            return (status, graphResponse.SuccessCount, graphResponse.UsersNotFound);
        }

        public async Task<(GraphUpdaterStatus Status, int SuccessCount, List<AzureADUser> UsersNotFound)> RemoveUsersFromGroupAsync(ICollection<AzureADUser> members, Guid targetGroupId, Guid runId, bool isInitialSync)
        {
            var stopwatch = Stopwatch.StartNew();
            var graphResponse = await _graphGroupRepository.RemoveUsersFromGroup(members, new AzureADGroup { ObjectId = targetGroupId });
            stopwatch.Stop();

            if (isInitialSync)
                _telemetryClient.TrackMetric(nameof(Metric.MembersRemovedFromOnboarding), graphResponse.SuccessCount);
            else
                _telemetryClient.TrackMetric(nameof(Metric.MembersRemoved), graphResponse.SuccessCount);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Removing {members.Count} users from group {targetGroupId} complete in {stopwatch.Elapsed.TotalSeconds} seconds. " +
                $"{members.Count / stopwatch.Elapsed.TotalSeconds} users removed per second.",
                RunId = runId
            });
            _telemetryClient.TrackMetric(nameof(Metric.GraphRemoveRatePerSecond), members.Count / stopwatch.Elapsed.TotalSeconds);

            var status = graphResponse.ResponseCode == ResponseCode.Error ? GraphUpdaterStatus.Error : GraphUpdaterStatus.Ok;
            return (status, graphResponse.SuccessCount, graphResponse.UsersNotFound);
        }

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            return await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(email, groupObjectId);
        }

        public async Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            return await _graphGroupRepository.GetGroupOwnersAsync(groupObjectId, top);
        }
    }
}