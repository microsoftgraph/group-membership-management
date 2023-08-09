// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using Models.ServiceBus;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;

namespace Services
{
    public class PlaceMembershipObtainerService
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IDatabaseSyncJobsRepository _syncJob;
        private readonly bool _isPlaceMembershipObtainerDryRunEnabled;

        public PlaceMembershipObtainerService(IGraphGroupRepository graphGroupRepository,
                                      IBlobStorageRepository blobStorageRepository,
                                      IDatabaseSyncJobsRepository syncJob,
                                      IDryRunValue dryRun
                                      )
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _syncJob = syncJob ?? throw new ArgumentNullException(nameof(syncJob));
            _isPlaceMembershipObtainerDryRunEnabled = dryRun.DryRunEnabled;
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
                NextPageUrl = response.nextPageUrl
            };
        }

        public async Task<PlaceInformation> GetWorkSpacesAsync(string url, int top, int skip)
        {
            var response = await _graphGroupRepository.GetWorkSpacesPageAsync(url, top, skip);
            return new PlaceInformation
            {
                Users = response.users,
                NextPageUrl = response.nextPageUrl
            };
        }

        public async Task<UserInformation> GetUsersAsync(string url)
        {
            var result = await _graphGroupRepository.GetFirstMembersPageAsync(url);
            return new UserInformation
            {
                Users = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                NextPageUrl = result.nextPageUrl
            };
        }

        public async Task<UserInformation> GetNextUsersAsync(string nextPageUrl)
        {
            var result = await _graphGroupRepository.GetNextMembersPageAsync(nextPageUrl);
            return new UserInformation
            {
                Users = result.users,
                NonUserGraphObjects = result.nonUserGraphObjects,
                NextPageUrl = result.nextPageUrl
            };
        }

        public async Task<string> SendMembershipAsync(SyncJob syncJob, List<AzureADUser> allUsers, int currentPart, bool exclusionary)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            var groupMembership = new GroupMembership
            {
                SourceMembers = allUsers ?? new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = syncJob.TargetOfficeGroupId },
                RunId = runId,
                Exclusionary = exclusionary,
                SyncJobId = syncJob.Id,                
                MembershipObtainerDryRunEnabled = _isPlaceMembershipObtainerDryRunEnabled
            };

            var timeStamp = DateTime.UtcNow.ToString("MMddyyyy-HHmm");
            var fileName = $"/{syncJob.TargetOfficeGroupId}/{timeStamp}_{runId}_PlaceMembership_{currentPart}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonConvert.SerializeObject(groupMembership));

            return fileName;
        }

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status)
        {
            await _syncJob.UpdateSyncJobStatusAsync(new[] { job }, status);
        }
    }
}