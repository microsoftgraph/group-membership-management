// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Graph.Models;
using Models.Helpers;
using Models.ServiceBus;
using Newtonsoft.Json;
using Repositories.BlobStorage;
using System.Collections.Generic;
using System.Linq;

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
            
            var json = JsonConvert.DeserializeObject<GroupMembership>(request.FileContent);
            var cacheMembers = json.SourceMembers.Distinct().ToList();

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

            var timeStamp = request.Timestamp;
            var groupMembership = new GroupMembership
            {
                SourceMembers = newUsers ?? new List<AzureADUser>()
            };
            var fileName = $"/cache/{request.GroupId}_{timeStamp}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonConvert.SerializeObject(groupMembership));

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = request.RunId,
                Message = $"Successfully uploaded {newUsers.Count} users from group {request.GroupId} to cache."
            });

            await _loggingRepository.LogMessageAsync(
                new LogMessage
                {
                    Message = $"{nameof(CacheUpdaterFunction)} New count in cache/{request.GroupId}: {newUsers.Count}",
                    RunId = request.RunId
                },
                VerbosityLevel.DEBUG);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUpdaterFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}