// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.Graph;
using Models;
using Newtonsoft.Json;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;

namespace Services
{
    public class AzureMembershipProviderService
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ILoggingRepository _log;
        private readonly IMailRepository _mail;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly ISyncJobRepository _syncJob;
        private readonly bool _isAzureMembershipProviderDryRunEnabled;

        public AzureMembershipProviderService(IGraphGroupRepository graphGroupRepository,
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
            _isAzureMembershipProviderDryRunEnabled = dryRun.DryRunEnabled;
        }

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

        public async Task<PlaceInformation> GetRoomsAsync(string url, int top, int skip)
        {
            var response = await _graphGroupRepository.GetRoomsPageAsync(url, top, skip);
            return new PlaceInformation
            {
                Users = response.users,
                UsersFromPage = response.usersFromGroup
            };
        }

        public async Task<PlaceInformation> GetWorkSpacesAsync(string url, int top, int skip)
        {
            var response = await _graphGroupRepository.GetWorkSpacesPageAsync(url, top, skip);
            return new PlaceInformation
            {
                Users = response.users,
                UsersFromPage = response.usersFromGroup
            };
        }

        public async Task<UserInformation> GetUsersAsync(string url)
        {
            var result = await _graphGroupRepository.GetFirstMembersPageAsync(url);
            return new UserInformation
            {
                Users = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                NextPageUrl = result.nextPageUrl,
                UsersFromPage = result.usersFromGroup
            };
        }

        public async Task<UserInformation> GetNextUsersAsync(string nextPageUrl, IGraphServiceUsersCollectionPage usersFromPage)
        {
            var result = await _graphGroupRepository.GetNextMembersPageAsync(nextPageUrl, usersFromPage);
            return new UserInformation
            {
                Users = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                NextPageUrl = result.nextPageUrl,
                UsersFromPage = result.usersFromGroup
            };
        }

        public async Task<string> SendMembershipAsync(SyncJob syncJob, List<AzureADUser> allusers, int currentPart, bool exclusionary)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            var groupMembership = new GroupMembership
            {
                SourceMembers = allusers ?? new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = syncJob.TargetOfficeGroupId },
                RunId = runId,
                Exclusionary = exclusionary,
                SyncJobRowKey = syncJob.RowKey,
                SyncJobPartitionKey = syncJob.PartitionKey,
                MembershipObtainerDryRunEnabled = _isAzureMembershipProviderDryRunEnabled
            };

            var timeStamp = syncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss");
            var fileName = $"/{syncJob.TargetOfficeGroupId}/{timeStamp}_{runId}_AzureMembershipProvider_{currentPart}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonConvert.SerializeObject(groupMembership));

            return fileName;
        }

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status)
        {
            await _syncJob.UpdateSyncJobStatusAsync(new[] { job }, status);
        }
    }
}