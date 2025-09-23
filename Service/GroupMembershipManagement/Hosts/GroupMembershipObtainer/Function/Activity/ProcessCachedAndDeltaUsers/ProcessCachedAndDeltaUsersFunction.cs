// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class ProcessCachedAndDeltaUsersFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public ProcessCachedAndDeltaUsersFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator, IBlobStorageRepository blobStorageRepository)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(ProcessCachedAndDeltaUsersFunction))]
        public async Task<ProcessCachedAndDeltaUsersResponse> RunAsync([ActivityTrigger] ProcessCachedAndDeltaUsersRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(ProcessCachedAndDeltaUsersFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            if (!string.IsNullOrEmpty(request.CacheFilePath))
            {
                var membershipFilePath = string.Empty;

                // Get the cached users
                var parser = new Func<string, Guid>(s => Guid.Parse(s));
                HashSet<Guid> cachedUsers = await _blobStorageRepository.ReadValuesFromBlobAsync(request.CacheFilePath, parser);

                await _log.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"Before delta link updates, cache for group {request.SourceGroupId} has {cachedUsers.Count} users."
                }, VerbosityLevel.DEBUG);

                // Get the delta users to add and remove from blob storage
                string prefixAdds = $"{request.TargetGroupId}/userUploads/deltaLink/adds/{request.RunId}_GroupMembership_{request.CurrentPart}";
                var blobResultAdds = await _blobStorageRepository.ReadBlobsAsync(prefixAdds);
                var deltaUsersToAdd = blobResultAdds;

                string prefixRemoves = $"{request.TargetGroupId}/userUploads/deltaLink/removes/{request.RunId}_GroupMembership_{request.CurrentPart}";
                var blobResultRemoves = await _blobStorageRepository.ReadBlobsAsync(prefixRemoves);
                var deltaUsersToRemove = blobResultRemoves;

                if (deltaUsersToAdd.Count == 0)
                {
                    await _log.LogMessageAsync(new LogMessage
                    {
                        RunId = request.RunId,
                        Message = $"No delta users to add found for the group {request.SourceGroupId} in blob storage."
                    }, VerbosityLevel.DEBUG);
                }

                if (deltaUsersToRemove.Count == 0)
                {
                    await _log.LogMessageAsync(new LogMessage
                    {
                        RunId = request.RunId,
                        Message = $"No delta users to remove for the group {request.SourceGroupId} found in blob storage."
                    }, VerbosityLevel.DEBUG);
                }

                // Update cache based on delta users
                if (deltaUsersToAdd.Count > 0 || deltaUsersToRemove.Count > 0)
                {

                    foreach (var user in deltaUsersToAdd.Select(x => x.ObjectId))
                    {
                        if (!cachedUsers.Contains(user))
                        {
                            cachedUsers.Add(user);
                        }
                    }
                    foreach (var user in deltaUsersToRemove.Select(x => x.ObjectId))
                    {
                        if (cachedUsers.Contains(user))
                        {
                            cachedUsers.Remove(user);
                        }
                    }
                    await _log.LogMessageAsync(new LogMessage
                    {
                        RunId = request.RunId,
                        Message = $"After delta link call for group {request.SourceGroupId} - Added {deltaUsersToAdd.Count} delta users, Removed {deltaUsersToRemove.Count} delta users. Total users in cache {cachedUsers.Count}."
                    }, VerbosityLevel.DEBUG);
                }

                // Check if cache matches the AAD group
                if (cachedUsers.Count == request.CountOfUsersFromAADGroup)
                {
                    await _log.LogMessageAsync(new LogMessage
                    {
                        RunId = request.RunId,
                        Message = $"After delta link updates, number of users from group {request.SourceGroupId} {request.CountOfUsersFromAADGroup} and cache {cachedUsers.Count} are equal. Uploading membership, cache, and delta link files."
                    }, VerbosityLevel.DEBUG);

                    var utcNow = DateTime.UtcNow;
                    var timeStamp = utcNow.ToString("MMddyyyy-HHmm");

                    // Upload membership file
                    membershipFilePath = $"{request.TargetGroupId}/{timeStamp}_{request.RunId}_GroupMembership_{request.CurrentPart}.json";
                    await UploadMembershipFileAsync(cachedUsers, request, membershipFilePath);

                    // Upload the updated cache
                    var fileName = CacheFileNaming.BuildCacheFileName(request.SourceGroupId, utcNow);
                    var metadata = new Dictionary<string, string>
                    {
                        { "RunId", request.RunId.ToString() },
                        { "NumberOfUsers", cachedUsers.Count.ToString() }
                    };
                    await _blobStorageRepository.UploadFileAsync(fileName, string.Join(Environment.NewLine, cachedUsers), metadata);

                    // Update delta link and upload
                    var deltaLinkFile = $"/cache/delta_{request.SourceGroupId}_{timeStamp}.json";
                    await _blobStorageRepository.UploadFileAsync(deltaLinkFile, request.DeltaUrl);

                    await _log.LogMessageAsync(new LogMessage
                    {
                        RunId = request.RunId,
                        Message = $"After delta link call, successfully uploaded {cachedUsers.Count} users + delta link {request.DeltaUrl} to cache for group {request.SourceGroupId}."
                    });

                    // Delete blobs for adds and removes
                    await _blobStorageRepository.DeleteFileAsync(prefixAdds);
                    await _blobStorageRepository.DeleteFileAsync(prefixRemoves);
                }
                else
                {
                    await _log.LogMessageAsync(new LogMessage
                    {
                        RunId = request.RunId,
                        Message = $"After delta link updates for group {request.SourceGroupId}. Cache mismatch: cached={cachedUsers.Count}, actual={request.CountOfUsersFromAADGroup}. Running initial delta call."
                    });
                }
                return new ProcessCachedAndDeltaUsersResponse
                {
                    MembershipFilePath = membershipFilePath,
                    CacheMatchesGroupCount = cachedUsers.Count == request.CountOfUsersFromAADGroup,
                    CacheCount = cachedUsers.Count
                };
            }
            else
            {
                await _log.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"{nameof(ProcessCachedAndDeltaUsersFunction)} No cache file path provided for group {request.SourceGroupId}."
                }, VerbosityLevel.DEBUG);

                return new ProcessCachedAndDeltaUsersResponse
                {
                    MembershipFilePath = string.Empty,
                    CacheMatchesGroupCount = false,
                    CacheCount = 0
                };
            }
        }

        private async Task UploadMembershipFileAsync(HashSet<Guid> cachedUsers, ProcessCachedAndDeltaUsersRequest request, string membershipFilePath)
        {
            var sourceMembers = cachedUsers.Select(u => new AzureADUser { ObjectId = u }).ToList();
            var groupMembership = new GroupMembership
            {
                SourceMembers = sourceMembers ?? new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = request.TargetGroupId },
                RunId = request.RunId,
                Exclusionary = request.Exclusionary,
                SyncJobId = request.SyncJob.Id,
                MembershipObtainerDryRunEnabled = request.SyncJob.IsDryRunEnabled,
                Query = request.SyncJob.Query
            };

            await _blobStorageRepository.UploadFileAsync(membershipFilePath, JsonSerializer.Serialize(groupMembership));
        }
    }
}