// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

        [FunctionName(nameof(CacheUpdaterFunction))]
        public async Task UpdateCacheAsync
            ([ActivityTrigger] CacheUpdaterRequest request)
        {

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUpdaterFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUpdaterFunction)} {request.UserIds.Count} users to remove from cache/{request.GroupId}", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var newUsers = await GetUsersFromCacheAsync(request);
            await UploadCacheFileAsync(request, newUsers);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUpdaterFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }

        private async Task<List<AzureADUser>> GetUsersFromCacheAsync(CacheUpdaterRequest request)
        {
            var parser = new Func<string, AzureADUser>(s =>
            {
                var id = Guid.Parse(s);
                var user = new AzureADUser { ObjectId = id };
                return user;
            });

            HashSet<AzureADUser> cacheMembers = await _blobStorageRepository.ReadValuesFromBlobAsync(request.CacheFilePath, parser);

            await _loggingRepository.LogMessageAsync(
            new LogMessage
            {
                Message = $"{nameof(CacheUpdaterFunction)} Earlier count in cache/{request.GroupId}: {cacheMembers.Count}",
                RunId = request.RunId
            },
            VerbosityLevel.DEBUG);

            var newUsers = cacheMembers.Except(request.UserIds).ToList();

            await _loggingRepository.LogMessageAsync(
            new LogMessage
            {
                Message = $"{nameof(CacheUpdaterFunction)} {newUsers.Count} newUsers to add to cache/{request.GroupId}",
                RunId = request.RunId
            },
            VerbosityLevel.DEBUG);

            return newUsers;
        }

        private async Task UploadCacheFileAsync(CacheUpdaterRequest request, List<AzureADUser> newUsers)
        {
            var fileName = CacheFileNaming.BuildCacheFileName(request.GroupId, request.Timestamp);
            await _blobStorageRepository.UploadFileAsync(fileName, string.Join(Environment.NewLine, newUsers.Select(x => x.ObjectId)));
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = request.RunId,
                Message = $"Successfully uploaded {newUsers.Count} users from group {request.GroupId} to cache {fileName}."
            });
        }
    }
}