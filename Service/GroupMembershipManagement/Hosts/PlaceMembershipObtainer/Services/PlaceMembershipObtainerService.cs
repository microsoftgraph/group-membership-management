// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System.Text.Json;
using Services.Contracts;

namespace Services
{
    public class PlaceMembershipObtainerService
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly bool _isPlaceMembershipObtainerDryRunEnabled;

        public PlaceMembershipObtainerService(IGraphGroupRepository graphGroupRepository,
                                      IBlobStorageRepository blobStorageRepository,
                                      ISyncJobStatusService syncJobStatusService,
                                      IDatabaseGroupsRepository databaseGroupsRepository,
                                      IDatabaseChannelsRepository databaseChannelsRepository,
                                      IDryRunValue dryRun
                                      )
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _isPlaceMembershipObtainerDryRunEnabled = dryRun.DryRunEnabled;
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

        public async Task<Guid> GetGroupIdAsync(SyncJob syncJob)
        {
            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                var channel = syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
                return channel.GroupId;
            }
            else if (syncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                var group = syncJob.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(syncJob.Id);
                return group.GroupId;
            }
            return Guid.Empty;
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

        public async Task<string> SendMembershipAsync(SyncJob syncJob, Guid groupId, List<AzureADUser> allUsers, int currentPart, bool exclusionary)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            var groupMembership = new GroupMembership
            {
                SourceMembers = allUsers ?? new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = groupId },
                RunId = runId,
                Exclusionary = exclusionary,
                SyncJobId = syncJob.Id,
                MembershipObtainerDryRunEnabled = _isPlaceMembershipObtainerDryRunEnabled
            };

            var timeStamp = DateTime.UtcNow.ToString("MMddyyyy-HHmm");
            var fileName = $"/{groupId}/{timeStamp}_{runId}_PlaceMembership_{currentPart}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(groupMembership));

            return fileName;
        }

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status)
        {
            var currentDate = DateTime.UtcNow;
            var updatedBy = "PlaceMembershipObtainer";
            var history = new SyncJobHistory
            {
                SyncJobId = job.Id,
                RunId = job.RunId ?? Guid.Empty,
                Status = status.ToString(),
                UpdatedByFunction = updatedBy,
                EndTime = status != SyncStatus.InProgress ? currentDate : null,              
                UpdatedAt = currentDate
            };

            await _syncJobStatusService.UpdateJobStatusAsync(job, status, history, functionName: updatedBy);
        }
    }
}