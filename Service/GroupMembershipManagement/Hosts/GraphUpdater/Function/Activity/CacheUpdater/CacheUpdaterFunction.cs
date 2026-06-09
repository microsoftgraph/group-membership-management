// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<CacheUpdaterFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public CacheUpdaterFunction(ILogger<CacheUpdaterFunction> logger, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(CacheUpdaterFunction))]
        public async Task UpdateCacheAsync
            ([ActivityTrigger] CacheUpdaterRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(CacheUpdaterFunction));
            _logger.CacheUpdaterRemovingUsers(request.UserIds.Count, request.GroupId);

            var newUsers = await GetUsersFromCacheAsync(request);
            await UploadCacheFileAsync(request, newUsers);

            _logger.FunctionCompleted(nameof(CacheUpdaterFunction));
        }

        private async Task<HashSet<Guid>> GetUsersFromCacheAsync(CacheUpdaterRequest request)
        {
            var parser = new Func<string, Guid>(s => Guid.Parse(s));

            HashSet<Guid> cacheMembers = await _blobStorageRepository.ReadValuesFromBlobAsync(request.CacheFilePath, parser);

            _logger.CacheUpdaterEarlierCount(request.GroupId, cacheMembers.Count);

            // updates the cache members set in place
            cacheMembers.ExceptWith(request.UserIds);

            _logger.CacheUpdaterAddingUsers(cacheMembers.Count, request.GroupId);

            return cacheMembers;
        }

        private async Task UploadCacheFileAsync(CacheUpdaterRequest request, HashSet<Guid> newUsers)
        {
            var fileName = CacheFileNaming.BuildCacheFileName(request.GroupId, request.Timestamp);
            var metadata = new Dictionary<string, string>
            {
                { "RunId", request.SyncJob.RunId.GetValueOrDefault().ToString() },
                { "NumberOfUsers", newUsers.Count.ToString() }
            };
            await _blobStorageRepository.UploadCacheFromGuidsAsync(fileName, newUsers, metadata);

            // Delete old cache files, keeping only the latest one
            var cachePrefix = CacheFileNaming.BuildCacheFileNamePrefix(request.GroupId);
            await _blobStorageRepository.DeleteFilesByPrefixAsync(cachePrefix, excludeLatest: true);

            _logger.CacheUploadSuccess(newUsers.Count, request.GroupId, fileName);
        }
    }
}