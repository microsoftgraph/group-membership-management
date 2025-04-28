// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

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

        [FunctionName(nameof(ProcessCachedAndDeltaUsersFunction))]
        public async Task<ProcessCachedAndDeltaUsersResponse> RunAsync([ActivityTrigger] ProcessCachedAndDeltaUsersRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(ProcessCachedAndDeltaUsersFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            if (!string.IsNullOrEmpty(request.CacheFilePath))
            {
                var membershipFilePath = string.Empty;

                // Get the cache
                var blobResult = await _blobStorageRepository.DownloadCacheFileAsync(request.CacheFilePath);
                var cacheFileContent = blobResult.Content;
                var membership = JsonSerializer.Deserialize<GroupMembership>(cacheFileContent);
                var cachedUsers = membership?.SourceMembers.Distinct().ToList();

                // Get the delta users to add and remove from blob storage
                string prefixAdds = $"{request.TargetGroupId}/userUploads/deltaLink/adds/{request.RunId}_GroupMembership";
                var blobResultAdds = await _blobStorageRepository.ReadBlobsAsync(prefixAdds);
                var deltaUsersToAdd = blobResultAdds;

                string prefixRemoves = $"{request.TargetGroupId}/userUploads/deltaLink/removes/{request.RunId}_GroupMembership";
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

                    foreach (var user in deltaUsersToAdd)
                    {
                        if (!cachedUsers.Contains(user))
                        {
                            cachedUsers.Add(user);
                        }
                    }
                    foreach (var user in deltaUsersToRemove)
                    {
                        if (cachedUsers.Contains(user))
                        {
                            cachedUsers.Remove(user);
                        }
                    }
                    await _log.LogMessageAsync(new LogMessage
                    {
                        RunId = request.RunId,
                        Message = $"Group {request.SourceGroupId}. Added {deltaUsersToAdd.Count} delta users, and removed {deltaUsersToRemove.Count} delta users. Total users in cache {cachedUsers.Count}."
                    }, VerbosityLevel.DEBUG);
                }

                // Check if cache matches the AAD group
                if (cachedUsers.Count == request.CountOfUsersFromAADGroup)
                {
                    await _log.LogMessageAsync(new LogMessage
                    {
                        RunId = request.RunId,
                        Message = $"Cache for group {request.SourceGroupId} has {cachedUsers.Count} users and group has {request.CountOfUsersFromAADGroup} users. Updating membership, cache, and delta link."
                    }, VerbosityLevel.DEBUG);


                    var timeStamp = DateTime.UtcNow.ToString("MMddyyyy-HHmm");

                    // Upload membership file
                    membershipFilePath = $"{request.TargetGroupId}/{timeStamp}_{request.RunId}_GroupMembership_{request.CurrentPart}.json";

                    var groupMembership = new GroupMembership
                    {
                        SourceMembers = cachedUsers ?? new List<AzureADUser>(),
                        Destination = new AzureADGroup { ObjectId = request.TargetGroupId },
                        RunId = request.RunId,
                        Exclusionary = request.Exclusionary,
                        SyncJobId = request.SyncJob.Id,
                        MembershipObtainerDryRunEnabled = request.SyncJob.IsDryRunEnabled,
                        Query = request.SyncJob.Query
                    };
                    
                    await _blobStorageRepository.UploadFileAsync(membershipFilePath, JsonSerializer.Serialize(groupMembership));


                    // Upload the updated cache
                    var cacheFile = $"/cache/{request.SourceGroupId}_{timeStamp}.json";
                    await _blobStorageRepository.UploadFileAsync(cacheFile, JsonSerializer.Serialize(groupMembership));

                    // Update delta link and upload
                    var deltaLinkFile = $"/cache/delta_{request.SourceGroupId}_{timeStamp}.json";
                    await _blobStorageRepository.UploadFileAsync(deltaLinkFile, request.DeltaUrl);

                    // Delete blobs for adds and removes, leave commented for testing 
                    //await _blobStorageRepository.DeleteFileAsync(prefixAdds);
                    //await _blobStorageRepository.DeleteFileAsync(prefixRemoves);
                }
                else
                {
                    await _log.LogMessageAsync(new LogMessage
                    {
                        RunId = request.RunId,
                        Message = $"Group {request.SourceGroupId}. Cache mismatch: cached={cachedUsers.Count}, actual={request.CountOfUsersFromAADGroup}. Running initial delta call."
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
    }
}