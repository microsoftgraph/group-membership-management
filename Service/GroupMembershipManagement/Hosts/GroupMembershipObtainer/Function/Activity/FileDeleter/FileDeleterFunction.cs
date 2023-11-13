// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.Helpers;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class FileDeleterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public FileDeleterFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [FunctionName(nameof(FileDeleterFunction))]
        public async Task DeleteFileAsync([ActivityTrigger] FileDeleterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
            await _blobStorageRepository.DeleteFilesAsync(request.FilePath);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
        }
    }
}