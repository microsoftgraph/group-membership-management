// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.Helpers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class FileUploaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public FileUploaderFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [FunctionName(nameof(FileUploaderFunction))]
        public async Task UploadFileAsync([ActivityTrigger] FileUploaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Uploading file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
            await _blobStorageRepository.UploadFileAsync(request.FilePath, TextCompressor.Decompress(request.Content));
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Uploaded file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
        }
    }
}
