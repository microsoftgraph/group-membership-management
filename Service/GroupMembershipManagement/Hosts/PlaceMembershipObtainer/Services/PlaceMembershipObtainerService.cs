// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
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
        private readonly IDestinationResolver _destinationResolver;
        private readonly bool _isPlaceMembershipObtainerDryRunEnabled;

        public PlaceMembershipObtainerService(IGraphGroupRepository graphGroupRepository,
                                      IBlobStorageRepository blobStorageRepository,
                                      ISyncJobStatusService syncJobStatusService,
                                      IDestinationResolver destinationResolver,
                                      IDryRunValue dryRun
                                      )
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
            _destinationResolver = destinationResolver ?? throw new ArgumentNullException(nameof(destinationResolver));
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
            var destination = await _destinationResolver.ResolveAsync(syncJob);
            return destination switch
            {
                ResolvedGroupDestination groupDestination => groupDestination.ObjectId,
                ResolvedTeamsChannelDestination channelDestination => channelDestination.TeamObjectId,
                _ => Guid.Empty
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

        public async Task<string> SendMembershipAsync(SyncJob syncJob, Guid groupId, List<AzureADUser> allUsers, int currentPart, bool exclusionary)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            allUsers?.Sort(CanonicalMemberComparer<AzureADUser>.Instance);
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