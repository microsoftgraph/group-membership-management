// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models.Entities;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class FileUploaderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public FileUploaderFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [FunctionName(nameof(FileUploaderFunction))]
        public async Task SendUsersAsync([ActivityTrigger] FileUploaderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(FileUploaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            var timeStamp = request.SyncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss");
            var groupMembership = new GroupMembership
            {
                SourceMembers = request.Users ?? new List<AzureADUser>()
            };
            var fileName = $"/cache/{request.ObjectId}_{timeStamp}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonConvert.SerializeObject(groupMembership));

            await _log.LogMessageAsync(new LogMessage
            {
                RunId = request.RunId,
                Message = $"Successfully uploaded {request.Users.Count} users from group {request.ObjectId} to cache."
            });
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(FileUploaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}