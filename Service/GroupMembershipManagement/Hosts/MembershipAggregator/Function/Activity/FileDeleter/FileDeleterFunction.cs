// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
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
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting file {request.FilePath}", RunId = request.RunId }, VerbosityLevel.DEBUG);
            await _blobStorageRepository.DeleteFileAsync(request.FilePath);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleted file {request.FilePath}", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}
