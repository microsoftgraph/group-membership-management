// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class CacheUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public CacheUpdaterFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(CacheUpdaterFunction))]
        public async Task UpdateCacheAsync
            ([ActivityTrigger] CacheUpdaterRequest request)
        {

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUpdaterFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUpdaterFunction)} {request.UserIds.Count} users to remove from cache/{request.GroupId}", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var newUsers = await GetUsersFromCacheAsync(request);
            await UploadCacheFileAsync(request, newUsers);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUpdaterFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }

        private async Task<HashSet<Guid>> GetUsersFromCacheAsync(CacheUpdaterRequest request)
        {
            var parser = new Func<string, Guid>(s => Guid.Parse(s));

            HashSet<Guid> cacheMembers = await _blobStorageRepository.ReadValuesFromBlobAsync(request.CacheFilePath, parser);

            await _loggingRepository.LogMessageAsync(
            new LogMessage
            {
                Message = $"{nameof(CacheUpdaterFunction)} Earlier count in cache/{request.GroupId}: {cacheMembers.Count}",
                RunId = request.RunId
            },
            VerbosityLevel.DEBUG);

            // updates the cache members set in place
            cacheMembers.ExceptWith(request.UserIds);

            await _loggingRepository.LogMessageAsync(
            new LogMessage
            {
                Message = $"{nameof(CacheUpdaterFunction)} {cacheMembers.Count} newUsers to add to cache/{request.GroupId}",
                RunId = request.RunId
            },
            VerbosityLevel.DEBUG);

            return cacheMembers;
        }

        private async Task UploadCacheFileAsync(CacheUpdaterRequest request, HashSet<Guid> newUsers)
        {
            var fileName = CacheFileNaming.BuildCacheFileName(request.GroupId, request.Timestamp);
            var metadata = new Dictionary<string, string>
            {
                { "RunId", request.RunId.ToString() },
                { "NumberOfUsers", newUsers.Count.ToString() }
            };
            await _blobStorageRepository.UploadCacheFromGuidsAsync(fileName, newUsers, metadata);

            // Delete old cache files, keeping only the latest one
            var cachePrefix = CacheFileNaming.BuildCacheFileNamePrefix(request.GroupId);
            await _blobStorageRepository.DeleteFilesByPrefixAsync(cachePrefix, excludeLatest: true);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = request.RunId,
                Message = $"Successfully uploaded {newUsers.Count} users from group {request.GroupId} to cache {fileName}."
            });
        }
    }
}