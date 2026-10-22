// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
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

        /// <summary>
        /// Applies the run's saved-delta additions and removals to the cached source membership and, when the
        /// result matches the group's count, publishes the part (or staging) file, the cache and the delta link.
        /// </summary>
        /// <remarks>
        /// The add/remove inputs are not deleted here; the sub-orchestrator deletes them after this activity
        /// completes, so a retried or duplicate execution reads the same inputs. When the request carries a
        /// <see cref="ProcessCachedAndDeltaUsersRequest.FileTimestamp"/>, the three outputs are named from it and
        /// written once: an execution that finds them already written keeps the existing blobs and succeeds.
        /// </remarks>
        [Function(nameof(ProcessCachedAndDeltaUsersFunction))]
        public async Task<ProcessCachedAndDeltaUsersResponse> RunAsync([ActivityTrigger] ProcessCachedAndDeltaUsersRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(ProcessCachedAndDeltaUsersFunction));

                if (!string.IsNullOrEmpty(request.CacheFilePath))
                {
                    var membershipFilePath = string.Empty;
                    var stagingPath = string.Empty;

                    // Get the cached users
                    var parser = new Func<string, Guid>(s => Guid.Parse(s));
                    HashSet<Guid> cachedUsers = await _blobStorageRepository.ReadValuesFromBlobAsync(request.CacheFilePath, parser);

                    _logger.CacheBeforeDeltaUpdate(request.SourceGroupId, cachedUsers.Count);

                    // Get the delta users to add and remove from blob storage
                    var runId = request.SyncJob.RunId.GetValueOrDefault();
                    string prefixAdds = BuildDeltaAddsPrefix(request.TargetGroupId, runId, request.CurrentPart);
                    var usersToAdd = new HashSet<Guid>();
                    var deltaUsersToAddCount = 0;
                    await foreach (var user in _blobStorageRepository.StreamUsersFromBlobsAsync(prefixAdds))
                    {
                        deltaUsersToAddCount++;
                        usersToAdd.Add(user.ObjectId);
                    }

                    string prefixRemoves = BuildDeltaRemovesPrefix(request.TargetGroupId, runId, request.CurrentPart);
                    var usersToRemove = new HashSet<Guid>();
                    var deltaUsersToRemoveCount = 0;
                    await foreach (var user in _blobStorageRepository.StreamUsersFromBlobsAsync(prefixRemoves))
                    {
                        deltaUsersToRemoveCount++;
                        usersToRemove.Add(user.ObjectId);
                    }

                    // An identifier both added and removed in one read is ambiguous: the order of the two
                    // changes is unknown, so the result cannot be trusted even when the count matches.
                    var overlapCount = usersToAdd.Count(usersToRemove.Contains);
                    cachedUsers.UnionWith(usersToAdd);
                    cachedUsers.ExceptWith(usersToRemove);

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

                    if (overlapCount > 0)
                    {
                        _logger.DeltaAddRemoveOverlap(request.SourceGroupId, overlapCount);
                        return new ProcessCachedAndDeltaUsersResponse
                        {
                            MembershipFilePath = string.Empty,
                            CacheMatchesGroupCount = false,
                            CacheCount = cachedUsers.Count,
                            StagingPath = string.Empty
                        };
                    }

                    // Check if cache matches the AAD group
                    if (cachedUsers.Count == request.CountOfUsersFromAADGroup)
                    {
                        _logger.CacheMatchesUploading(request.SourceGroupId, request.CountOfUsersFromAADGroup, cachedUsers.Count);

                        var fileTime = request.FileTimestamp ?? DateTime.UtcNow;
                        var timeStamp = fileTime.ToString("MMddyyyy-HHmm");

                        // Upload membership file
                        membershipFilePath = $"{request.TargetGroupId}/{timeStamp}_{runId}_GroupMembership_{request.CurrentPart}.json";
                        stagingPath = request.WriteStaging
                            ? GroupPartStoragePaths.BuildStagingPath(
                                request.TargetGroupId,
                                runId,
                                request.CurrentPart)
                            : string.Empty;

                        // Upload the updated cache
                        var fileName = CacheFileNaming.BuildCacheFileName(request.SourceGroupId, fileTime);
                        var metadata = new Dictionary<string, string>
                        {
                            { "RunId", runId.ToString() },
                            { "NumberOfUsers", cachedUsers.Count.ToString() }
                        };

                        // Update delta link and upload
                        var deltaLinkFile = $"/cache/delta_{request.SourceGroupId}_{timeStamp}.json";

                        if (request.FileTimestamp.HasValue)
                        {
                            await PublishWriteOnceAsync(
                                request,
                                cachedUsers,
                                request.WriteStaging ? stagingPath : membershipFilePath,
                                fileName,
                                metadata,
                                deltaLinkFile);
                        }
                        else
                        {
                            // Histories scheduled before FileTimestamp existed keep the overwriting writes.
                            await UploadMembershipFileAsync(
                                cachedUsers,
                                request,
                                request.WriteStaging ? stagingPath : membershipFilePath);
                            try
                            {
                                await _blobStorageRepository.UploadCacheFromGuidsAsync(fileName, cachedUsers, metadata);
                            }
                            catch (RequestFailedException ex) when (ex.ErrorCode == "InvalidBlockList")
                            {
                                _logger.CacheWriteRaceConditionSkipped(request.SourceGroupId);
                            }

                            await _blobStorageRepository.UploadFileAsync(deltaLinkFile, request.DeltaUrl);
                        }

                        _logger.DeltaLinkCacheUploaded(cachedUsers.Count, request.DeltaUrl, request.SourceGroupId);
                    }
                    else
                    {
                        _logger.CacheMismatchRunningInitialDelta(request.SourceGroupId, cachedUsers.Count, request.CountOfUsersFromAADGroup);
                    }
                    return new ProcessCachedAndDeltaUsersResponse
                    {
                        MembershipFilePath = membershipFilePath,
                        CacheMatchesGroupCount = cachedUsers.Count == request.CountOfUsersFromAADGroup,
                        CacheCount = cachedUsers.Count,
                        StagingPath = stagingPath
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

        /// <summary>The blob-name prefix of the saved-delta additions written for one run and part.</summary>
        public static string BuildDeltaAddsPrefix(Guid targetGroupId, Guid runId, int currentPart) =>
            $"{targetGroupId}/userUploads/deltaLink/adds/{runId}_GroupMembership_{currentPart}_";

        /// <summary>The blob-name prefix of the saved-delta removals written for one run and part.</summary>
        public static string BuildDeltaRemovesPrefix(Guid targetGroupId, Guid runId, int currentPart) =>
            $"{targetGroupId}/userUploads/deltaLink/removes/{runId}_GroupMembership_{currentPart}_";

        /// <summary>
        /// Writes the part (or staging) file, the cache and the delta link, each one write-once. Each blob is
        /// checked on its own: a rejected cache does not imply that the delta link was written by the same
        /// execution, so the delta link is also written only when absent.
        /// </summary>
        private async Task PublishWriteOnceAsync(
            ProcessCachedAndDeltaUsersRequest request,
            HashSet<Guid> cachedUsers,
            string membershipPath,
            string cachePath,
            Dictionary<string, string> cacheMetadata,
            string deltaLinkPath)
        {
            var membership = await _blobStorageRepository.UploadGroupMembershipFromGuidsWriteOnceAsync(
                membershipPath,
                cachedUsers,
                new AzureADGroup { ObjectId = request.TargetGroupId },
                request.SyncJob.RunId.GetValueOrDefault(),
                request.SyncJob.Id,
                request.Exclusionary,
                request.SyncJob.IsDryRunEnabled,
                request.SyncJob.Query);
            LogIfAlreadyWritten(membership, request.WriteStaging ? "staging" : "part", membershipPath, request.SourceGroupId);

            var cache = await _blobStorageRepository.UploadCacheFromGuidsWriteOnceAsync(
                cachePath,
                cachedUsers,
                cacheMetadata);
            LogIfAlreadyWritten(cache, "cache", cachePath, request.SourceGroupId);

            var deltaLink = await _blobStorageRepository.UploadFileWriteOnceAsync(
                deltaLinkPath,
                request.DeltaUrl);
            LogIfAlreadyWritten(deltaLink, "delta link", deltaLinkPath, request.SourceGroupId);
        }

        private void LogIfAlreadyWritten(WriteOnceResult result, string blobKind, string path, Guid groupId)
        {
            if (result.AlreadyExisted)
            {
                _logger.WriteOnceBlobAlreadyWritten(blobKind, path, groupId);
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
