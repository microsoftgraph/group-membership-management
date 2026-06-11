// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class ProcessCachedAndDeltaUsersFunction
    {
        private readonly ILogger<ProcessCachedAndDeltaUsersFunction> _logger;
        private readonly SGMembershipCalculator _calculator;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public ProcessCachedAndDeltaUsersFunction(ILogger<ProcessCachedAndDeltaUsersFunction> logger, SGMembershipCalculator calculator, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(ProcessCachedAndDeltaUsersFunction))]
        public async Task<ProcessCachedAndDeltaUsersResponse> RunAsync([ActivityTrigger] ProcessCachedAndDeltaUsersRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(ProcessCachedAndDeltaUsersFunction));

                if (!string.IsNullOrEmpty(request.CacheFilePath))
                {
                    var membershipFilePath = string.Empty;

                    // Get the cached users
                    var parser = new Func<string, Guid>(s => Guid.Parse(s));
                    HashSet<Guid> cachedUsers = await _blobStorageRepository.ReadValuesFromBlobAsync(request.CacheFilePath, parser);

                    _logger.CacheBeforeDeltaUpdate(request.SourceGroupId, cachedUsers.Count);

                    // Get the delta users to add and remove from blob storage
                    var runId = request.SyncJob.RunId.GetValueOrDefault();
                    string prefixAdds = $"{request.TargetGroupId}/userUploads/deltaLink/adds/{runId}_GroupMembership_{request.CurrentPart}_";
                    var deltaUsersToAddCount = 0;
                    await foreach (var user in _blobStorageRepository.StreamUsersFromBlobsAsync(prefixAdds))
                    {
                        deltaUsersToAddCount++;
                        cachedUsers.Add(user.ObjectId);
                    }

                    string prefixRemoves = $"{request.TargetGroupId}/userUploads/deltaLink/removes/{runId}_GroupMembership_{request.CurrentPart}_";
                    var deltaUsersToRemoveCount = 0;
                    await foreach (var user in _blobStorageRepository.StreamUsersFromBlobsAsync(prefixRemoves))
                    {
                        deltaUsersToRemoveCount++;
                        cachedUsers.Remove(user.ObjectId);
                    }

                    if (deltaUsersToAddCount == 0)
                    {
                        _logger.NoDeltaUsersToAdd(request.SourceGroupId);
                    }

                    if (deltaUsersToRemoveCount == 0)
                    {
                        _logger.NoDeltaUsersToRemove(request.SourceGroupId);
                    }

                    // Update cache based on delta users
                    if (deltaUsersToAddCount > 0 || deltaUsersToRemoveCount > 0)
                    {
                        _logger.DeltaLinkUpdateSummary(request.SourceGroupId, deltaUsersToAddCount, deltaUsersToRemoveCount, cachedUsers.Count);
                    }

                    // Check if cache matches the AAD group
                    if (cachedUsers.Count == request.CountOfUsersFromAADGroup)
                    {
                        _logger.CacheMatchesUploading(request.SourceGroupId, request.CountOfUsersFromAADGroup, cachedUsers.Count);

                        var utcNow = DateTime.UtcNow;
                        var timeStamp = utcNow.ToString("MMddyyyy-HHmm");

                        // Upload membership file
                        membershipFilePath = $"{request.TargetGroupId}/{timeStamp}_{runId}_GroupMembership_{request.CurrentPart}.json";
                        await UploadMembershipFileAsync(cachedUsers, request, membershipFilePath);

                        // Upload the updated cache
                        var fileName = CacheFileNaming.BuildCacheFileName(request.SourceGroupId, utcNow);
                        var metadata = new Dictionary<string, string>
                        {
                            { "RunId", runId.ToString() },
                            { "NumberOfUsers", cachedUsers.Count.ToString() }
                        };
                        await _blobStorageRepository.UploadCacheFromGuidsAsync(fileName, cachedUsers, metadata);

                        // Update delta link and upload
                        var deltaLinkFile = $"/cache/delta_{request.SourceGroupId}_{timeStamp}.json";
                        await _blobStorageRepository.UploadFileAsync(deltaLinkFile, request.DeltaUrl);

                        _logger.DeltaLinkCacheUploaded(cachedUsers.Count, request.DeltaUrl, request.SourceGroupId);

                        // Delete blobs for adds and removes
                        await _blobStorageRepository.DeleteFileAsync(prefixAdds);
                        await _blobStorageRepository.DeleteFileAsync(prefixRemoves);
                    }
                    else
                    {
                        _logger.CacheMismatchRunningInitialDelta(request.SourceGroupId, cachedUsers.Count, request.CountOfUsersFromAADGroup);
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
                    _logger.NoCacheFilePath(nameof(ProcessCachedAndDeltaUsersFunction), request.SourceGroupId);

                    return new ProcessCachedAndDeltaUsersResponse
                    {
                        MembershipFilePath = string.Empty,
                        CacheMatchesGroupCount = false,
                        CacheCount = 0
                    };
                }
            }
        }

        private async Task UploadMembershipFileAsync(HashSet<Guid> cachedUsers, ProcessCachedAndDeltaUsersRequest request, string membershipFilePath)
        {
            await _blobStorageRepository.UploadGroupMembershipFromGuidsAsync(
                membershipFilePath,
                cachedUsers,
                new AzureADGroup { ObjectId = request.TargetGroupId },
                request.SyncJob.RunId.GetValueOrDefault(),
                request.SyncJob.Id,
                request.Exclusionary,
                request.SyncJob.IsDryRunEnabled,
                request.SyncJob.Query);
        }
    }
}