// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Helpers;
using Models.Notifications;
using Models.ServiceBus;
using Models.SyncJobHistory;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class SGMembershipCalculator
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ILogger<SGMembershipCalculator> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IDestinationResolver _destinationResolver;
        private readonly bool _isGroupMembershipDryRunEnabled;
        private readonly IServiceBusQueueRepository _notificationsQueueRepository;
        private readonly IDatabaseDestinationAttributesRepository _databaseDestinationAttributesRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public SGMembershipCalculator(IGraphGroupRepository graphGroupRepository,
                                      IBlobStorageRepository blobStorageRepository,
                                      IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                                      IDestinationResolver destinationResolver,
                                      IServiceBusQueueRepository notificationsQueueRepository,
                                      IDatabaseDestinationAttributesRepository databaseDestinationAttributesRepository,
                                      ILogger<SGMembershipCalculator> logger,
                                      IDryRunValue dryRun,
                                      ISyncJobStatusService syncJobStatusService
                                      )
        {
            _graphGroupRepository = graphGroupRepository;
            _blobStorageRepository = blobStorageRepository;
            _logger = logger;
            _databaseSyncJobsRepository = databaseSyncJobsRepository;
            _destinationResolver = destinationResolver ?? throw new ArgumentNullException(nameof(destinationResolver));
            _notificationsQueueRepository = notificationsQueueRepository;
            _databaseDestinationAttributesRepository = databaseDestinationAttributesRepository;
            _isGroupMembershipDryRunEnabled = dryRun.DryRunEnabled;
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
        }

        private const int NumberOfGraphRetries = 5;
        private const string EmailSubject = "EmailSubject";

        public object GraphRepository { get; set; }

        public async Task<PolicyResult<bool>> GroupExistsAsync(Guid objectId, Guid runId)
        {
            var graphRetryPolicy = Policy.Handle<HttpRequestException>().Or<SocketException>().WaitAndRetryAsync(NumberOfGraphRetries, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                   onRetry: (ex, sleepDuration) =>
                   {
                       _logger.TransientRetryException(ex, sleepDuration, NumberOfGraphRetries);
                   });

            return await graphRetryPolicy.ExecuteAndCaptureAsync(() => _graphGroupRepository.GroupExists(objectId));
        }

        public async Task<DeltaMemberPage> GetFirstDeltaLinkUsersPageAsync(Guid objectId, string deltaLink, int numberOfPages)
        {
            var result = await _graphGroupRepository.GetFirstDeltaLinkUsersPageAsync(objectId, deltaLink, numberOfPages);
            return new DeltaMemberPage
            {
                UsersToAdd = result.usersToAdd,
                UsersToRemove = result.usersToRemove,
                Counts = result.counts,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl
            };
        }

        public async Task<DeltaMemberPage> GetNextDeltaLinkUsersPageAsync(Guid objectId, string nextPageUrl, int numberOfPages)
        {
            var result = await _graphGroupRepository.GetNextDeltaLinkUsersPagesAsync(objectId, nextPageUrl, numberOfPages);
            return new DeltaMemberPage
            {
                UsersToAdd = result.usersToAdd,
                UsersToRemove = result.usersToRemove,
                Counts = result.counts,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl
            };
        }

        public async Task<int> GetGroupsCountAsync(Guid objectId)
        {
            return await _graphGroupRepository.GetGroupsCountAsync(objectId);
        }

        public async Task<int> GetUsersCountAsync(Guid objectId)
        {
            return await _graphGroupRepository.GetUsersCountAsync(objectId);
        }

        public async Task<int?> GetAgentUserCountAsync(Guid objectId)
        {
            return await _graphGroupRepository.GetAgentUserCountAsync(objectId);
        }

        public async Task<DeltaMemberPage> GetFirstDeltaUsersPageAsync(Guid objectId, Guid runId, int numberOfPages)
        {
            _logger.ReadingUsersFromGroup(objectId);
            var result = await _graphGroupRepository.GetFirstDeltaUsersPageAsync(objectId, numberOfPages);
            return new DeltaMemberPage
            {
                UsersToAdd = result.users,
                UsersToRemove = new List<AzureADUser>(),
                Counts = result.counts,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl
            };
        }

        public async Task<DeltaMemberPage> GetNextDeltaUsersPagesAsync(Guid objectId, string nextPageUrl, int numberOfPages)
        {
            var result = await _graphGroupRepository.GetNextDeltaUsersPagesAsync(objectId, nextPageUrl, numberOfPages);
            return new DeltaMemberPage
            {
                UsersToAdd = result.users,
                UsersToRemove = new List<AzureADUser>(),
                Counts = result.counts,
                NextPageUrl = result.nextPageUrl,
                DeltaUrl = result.deltaUrl
            };
        }

        public async Task<GroupMemberPage> GetFirstTransitiveMembersPageAsync(
            Guid objectId,
            Guid runId,
            IReadOnlyList<string> identityTypes = null)
        {
            _logger.ReadingUsersFromGroup(objectId);
            var result = await _graphGroupRepository.GetFirstTransitiveMembersPageAsync(objectId, identityTypes);
            return new GroupMemberPage
            {
                Users = result.users,
                Counts = result.counts,
                NextPageUrl = result.nextPageUrl
            };
        }

        public async Task<GroupMemberPage> GetNextTransitiveMembersPageAsync(
            Guid objectId,
            string nextPageUrl,
            IReadOnlyList<string> identityTypes = null)
        {
            var result = await _graphGroupRepository.GetNextTransitiveMembersPageAsync(
                objectId,
                nextPageUrl,
                identityTypes);
            return new GroupMemberPage
            {
                Users = result.users,
                Counts = result.counts,
                NextPageUrl = result.nextPageUrl
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

        public async Task<string> SendMembershipAsync(SyncJob syncJob, List<AzureADUser> allUsers, int currentPart, bool exclusionary)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            var targetOfficeGroupId = await GetGroupIdAsync(syncJob);
            allUsers?.Sort(CanonicalMemberComparer<AzureADUser>.Instance);
            var groupMembership = new GroupMembership
            {
                SourceMembers = allUsers ?? new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = targetOfficeGroupId },
                RunId = runId,
                Exclusionary = exclusionary,
                SyncJobId = syncJob.Id,
                MembershipObtainerDryRunEnabled = _isGroupMembershipDryRunEnabled,
                Query = syncJob.Query
            };

            var timeStamp = DateTime.UtcNow.ToString("MMddyyyy-HHmm");
            var fileName = $"/{targetOfficeGroupId}/{timeStamp}_{runId}_GroupMembership_{currentPart}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(groupMembership));

            return fileName;
        }

        public async Task<GroupMembershipFileResult> SendTransitiveAndDeltaMembershipAsync(
            SyncJob syncJob,
            Guid objectId,
            int currentPart,
            bool exclusionary,
            bool writeStaging = false,
            bool updateBeforeSyncCount = true,
            DateTime? fileTimestamp = null)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            var targetOfficeGroupId = await GetGroupIdAsync(syncJob);

            // build paths
            string prefix = $"{targetOfficeGroupId}/userUploads/{runId}_GroupMembership_{currentPart}_";
            var timeStamp = (fileTimestamp ?? DateTime.UtcNow).ToString("MMddyyyy-HHmm");
            var fileName = $"/{targetOfficeGroupId}/{timeStamp}_{runId}_GroupMembership_{currentPart}.json";
            var outputPath = writeStaging
                ? GroupPartStoragePaths.BuildStagingPath(targetOfficeGroupId, runId, currentPart)
                : fileName;

            // Stream merge: reads source blobs one at a time, deduplicates by ObjectId, writes directly to output
            int memberCount;
            if (fileTimestamp.HasValue)
            {
                // Write-once: a re-run after the page uploads were cleaned up must not replace the published
                // output with an empty or partial merge; it keeps and reports the existing blob instead.
                var result = await _blobStorageRepository.MergeAndStreamUserBlobsWriteOnceAsync(
                    prefix,
                    outputPath,
                    new AzureADGroup { ObjectId = targetOfficeGroupId },
                    runId,
                    syncJob.Id,
                    exclusionary,
                    _isGroupMembershipDryRunEnabled,
                    syncJob.Query);
                if (result.AlreadyExisted)
                {
                    _logger.WriteOnceBlobAlreadyWritten(writeStaging ? "staging" : "part", outputPath, objectId);
                }

                memberCount = result.Count;
            }
            else
            {
                memberCount = await _blobStorageRepository.MergeAndStreamUserBlobsAsync(
                    prefix,
                    outputPath,
                    new AzureADGroup { ObjectId = targetOfficeGroupId },
                    runId,
                    syncJob.Id,
                    exclusionary,
                    _isGroupMembershipDryRunEnabled,
                    syncJob.Query);
            }

            // If we're reading from the target group itself, store the before sync user count during transitive/delta call.
            // Status is deliberately passed as null: the intent of this call is to persist BeforeSyncUserCount on SyncJobHistory,
            // not to change SyncJob.Status. Passing null prevents overwriting a terminal status (e.g. SourceGroupNotFound)
            // that a concurrent sibling part may have already written.
            if (updateBeforeSyncCount && objectId == targetOfficeGroupId)
            {
                await UpdateSyncJobStatusAsync(syncJob, status: null, memberCount);
            }

            return new GroupMembershipFileResult
            {
                FilePath = fileName,
                MemberCount = memberCount
            };
        }

        /// <summary>
        /// Uploads the delta link. With <paramref name="fileTimestamp"/> the name comes from it and the blob is
        /// written once (an existing blob is kept); without it the current UTC time names an overwriting write.
        /// </summary>
        public async Task UploadDeltaLinkAsync(Guid id, string deltaLink, Guid runId, DateTime? fileTimestamp = null)
        {
            var timeStamp = (fileTimestamp ?? DateTime.UtcNow).ToString("MMddyyyy-HHmm");
            var fileName = $"/cache/delta_{id}_{timeStamp}.json";
            if (fileTimestamp.HasValue)
            {
                var result = await _blobStorageRepository.UploadFileWriteOnceAsync(fileName, deltaLink);
                if (result.AlreadyExisted)
                {
                    _logger.WriteOnceBlobAlreadyWritten("delta link", fileName, id);
                }
            }
            else
            {
                await _blobStorageRepository.UploadFileAsync(fileName, deltaLink);
            }

            _logger.DeltaLinkUploadedToCache(deltaLink, id);
        }

        /// <summary>
        /// Streams a membership file into the source group's cache. With <paramref name="fileTimestamp"/> the
        /// name comes from it and the blob is written once (an existing blob is kept); without it the current
        /// UTC time names an overwriting write.
        /// </summary>
        public async Task UploadCacheAsync(Guid id, Guid runId, GroupMembershipFileResult fileResult, DateTime? fileTimestamp = null)
        {
            var fileName = CacheFileNaming.BuildCacheFileName(id, fileTimestamp ?? DateTime.UtcNow);
            var metadata = new Dictionary<string, string>
            {
                { "RunId", runId.ToString() }
            };

            // Stream directly from membership file to cache file to avoid loading all GUIDs into memory
            int count;
            if (fileTimestamp.HasValue)
            {
                var result = await _blobStorageRepository.StreamMembershipToCacheWriteOnceAsync(fileResult.FilePath, fileName, metadata);
                if (result.AlreadyExisted)
                {
                    _logger.WriteOnceBlobAlreadyWritten("cache", fileName, id);
                }

                count = result.Count;
            }
            else
            {
                count = await _blobStorageRepository.StreamMembershipToCacheAsync(fileResult.FilePath, fileName, metadata);
            }

            _logger.CacheUploadedForGroup(count, id);
        }
        public async Task SaveDeltaUsersAsync(SyncJob syncJob, Guid id, List<AzureADUser> users, string deltaLink)
        {
            var timeStamp = DateTime.UtcNow.ToString("MMddyyyy-HHmm");
            var fileName = $"/cache/delta_{id}_{timeStamp}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, deltaLink);
            var groupMembership = new GroupMembership
            {
                SourceMembers = users ?? new List<AzureADUser>()
            };
            var datafileName = $"/cache/{id}_{timeStamp}.json";
            await _blobStorageRepository.UploadFileAsync(datafileName, JsonSerializer.Serialize(groupMembership));
        }

        public async Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters)
        {
            var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParameters }
            };
            var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
            var message = new ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());
            await _notificationsQueueRepository.SendMessageAsync(message);
            _logger.SentNotificationQueueMessage(message.MessageId);

        }

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus? status, int? beforeSyncUserCount = null)
        {
            var syncJob = await _databaseSyncJobsRepository.GetSyncJobAsync(job.Id);
            if (syncJob != null)
            {
                var history = new SyncJobHistory
                {
                    SyncJobId = syncJob.Id,
                    RunId = syncJob.RunId ?? Guid.Empty,
                    Status = status?.ToString() ?? syncJob.Status,
                    UpdatedByFunction = "GroupMembershipObtainer",
                    EndTime = status.HasValue && status.Value != SyncStatus.InProgress ? DateTime.UtcNow : null,
                    UpdatedAt = DateTime.UtcNow,
                    BeforeSyncUserCount = beforeSyncUserCount
                };

                await _syncJobStatusService.UpdateJobStatusAsync(syncJob, status, history, "GroupMembershipObtainer");
            }
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }
        public async Task<string> GetDestinationNameAsync(SyncJob job)
        {
            var objectId = await GetGroupIdAsync(job);
            // Try to get the name from the DestinationNames table first

            var destinationName = await _databaseDestinationAttributesRepository.GetDestinationName(job);

            if (destinationName != null)
            {
                return destinationName;
            }

            _logger.DestinationNameNotInDatabase();

            if (job.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                return await _graphGroupRepository.GetGroupNameAsync(objectId);
            }

            return null;

        }
    }
}
