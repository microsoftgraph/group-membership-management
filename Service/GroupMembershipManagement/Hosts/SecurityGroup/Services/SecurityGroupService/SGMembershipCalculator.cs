// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.Graph;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class SGMembershipCalculator
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ILoggingRepository _log;
        private readonly IMailRepository _mail;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly ISyncJobRepository _syncJob;
        private readonly bool _isSecurityGroupDryRunEnabled;

        public SGMembershipCalculator(IGraphGroupRepository graphGroupRepository,
                                      IBlobStorageRepository blobStorageRepository,
                                      IMailRepository mail,
                                      IEmailSenderRecipient emailSenderAndRecipients,
                                      ISyncJobRepository syncJob,
                                      ILoggingRepository logging,
                                      IDryRunValue dryRun
                                      )
        {
            _graphGroupRepository = graphGroupRepository;
            _blobStorageRepository = blobStorageRepository;
            _log = logging;
            _mail = mail;
            _syncJob = syncJob;
            _emailSenderAndRecipients = emailSenderAndRecipients;
            _isSecurityGroupDryRunEnabled = dryRun.DryRunEnabled;
        }

        private const int NumberOfGraphRetries = 5;
        private AsyncRetryPolicy _graphRetryPolicy;
        private const string EmailSubject = "EmailSubject";
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

        public AzureADGroup[] ReadSourceGroups(string ids)
        {
            return ids.Split(';').Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty)
                                           .Where(x => x != Guid.Empty)
                                           .Select(x => new AzureADGroup { ObjectId = x }).ToArray();
        }

        public async Task<PolicyResult<bool>> GroupExistsAsync(Guid objectId, Guid runId)
        {
            // make this fresh every time because the lambda has to capture the run ID
            _graphRetryPolicy = Policy.Handle<SocketException>().WaitAndRetryAsync(NumberOfGraphRetries, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                   onRetry: async (ex, count) =>
                   {
                       await _log.LogMessageAsync(new LogMessage
                       {
                           Message = $"Got a transient SocketException. Retrying. This was try {count} out of {NumberOfGraphRetries}.\n" + ex.ToString(),
                           RunId = runId
                       });
                   });

            return await _graphRetryPolicy.ExecuteAndCaptureAsync(() => _graphGroupRepository.GroupExists(objectId));
        }

        public async Task<DeltaGroupInformation> GetFirstDeltaUsersPageAsync(string deltaLink)
        {
            var result = await _graphGroupRepository.GetFirstDeltaUsersPageAsync(deltaLink);
            return new DeltaGroupInformation
            {
                UsersToAdd = result.usersToAdd,
                UsersToRemove = result.usersToRemove,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl,
                UsersFromGroup = result.usersFromGroup

            };
        }

        public async Task<DeltaGroupInformation> GetNextDeltaUsersPageAsync(string nextPageUrl, IGroupDeltaCollectionPage page)
        {
            var result = await _graphGroupRepository.GetNextDeltaUsersPageAsync(nextPageUrl, page);
            return new DeltaGroupInformation
            {
                UsersToAdd = result.usersToAdd,
                UsersToRemove = result.usersToRemove,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl,
                UsersFromGroup = result.usersFromGroup
            };
        }

        public async Task<int> GetGroupsCountAsync(Guid objectId, Guid runId)
        {
            return await _graphGroupRepository.GetGroupsCountAsync(objectId);
        }

        public async Task<DeltaGroupInformation> GetFirstUsersPageAsync(Guid objectId, Guid runId)
        {
            await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Reading users from the group with ID {objectId}." });
            var result = await _graphGroupRepository.GetFirstUsersPageAsync(objectId);
            return new DeltaGroupInformation
            {
                UsersToAdd = result.users,                
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl,
                UsersFromGroup = result.usersFromGroup
            };
        }

        public async Task<DeltaGroupInformation> GetNextUsersPageAsync(string nextPageUrl, IGroupDeltaCollectionPage usersFromGroup)
        {
            var result = await _graphGroupRepository.GetNextUsersPageAsync(nextPageUrl, usersFromGroup);
            return new DeltaGroupInformation
            {
                UsersToAdd = result.users,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl,
                UsersFromGroup = result.usersFromGroup
            };
        }

        public async Task<GroupInformation> GetFirstTransitiveMembersPageAsync(Guid objectId, Guid runId)
        {
            await _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Reading users from the group with ID {objectId}." });
            var result = await _graphGroupRepository.GetFirstTransitiveMembersPageAsync(objectId);
            return new GroupInformation
            {
                Users = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                NextPageUrl = result.nextPageUrl,
                UsersFromGroup = result.usersFromGroup
            };
        }

        public async Task<GroupInformation> GetNextTransitiveMembersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)
        {
            var result = await _graphGroupRepository.GetNextTransitiveMembersPageAsync(nextPageUrl, usersFromGroup);
            return new GroupInformation
            {
                Users = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                NextPageUrl = result.nextPageUrl,
                UsersFromGroup = result.usersFromGroup
            };
        }

        public async Task<string> SendMembershipAsync(SyncJob syncJob, List<AzureADUser> allusers, int currentPart)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            var groupMembership = new GroupMembership
            {
                SourceMembers = allusers ?? new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = syncJob.TargetOfficeGroupId },
                RunId = runId,
                SyncJobRowKey = syncJob.RowKey,
                SyncJobPartitionKey = syncJob.PartitionKey,
                MembershipObtainerDryRunEnabled = _isSecurityGroupDryRunEnabled
            };

            var timeStamp = syncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss");
            var fileName = $"/{syncJob.TargetOfficeGroupId}/{timeStamp}_{runId}_SecurityGroup_{currentPart}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonConvert.SerializeObject(groupMembership));

            return fileName;
        }

        public async Task SaveDeltaUsersAsync(SyncJob syncJob, Guid id,  List<AzureADUser> users, string deltaLink)
        {
            var timeStamp = syncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss");
            var fileName = $"/cache/delta_{id}_{timeStamp}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, deltaLink);
            var groupMembership = new GroupMembership
            {
                SourceMembers = users ?? new List<AzureADUser>()
            };
            var datafileName = $"/cache/{id}_{timeStamp}.json";
            await _blobStorageRepository.UploadFileAsync(datafileName, JsonConvert.SerializeObject(groupMembership));
        }

        public async Task SendEmailAsync(SyncJob job, Guid runId, string content, string[] additionalContentParams)
        {
            await _mail.SendMailAsync(new EmailMessage
            {
                Subject = EmailSubject,
                Content = content,
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = job.Requestor,
                CcEmailAddresses = _emailSenderAndRecipients.SyncDisabledCCAddresses,
                AdditionalContentParams = additionalContentParams
            }, runId);
        }

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status)
        {
            await _syncJob.UpdateSyncJobStatusAsync(new[] { job }, status);
        }
    }
}