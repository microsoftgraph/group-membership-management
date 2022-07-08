// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class FileDownloaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public FileDownloaderFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [FunctionName(nameof(FileDownloaderFunction))]
        public async Task<(string FilePath, string Content)> DownloadFileAsync([ActivityTrigger] FileDownloaderRequest request)
        {
            var syncJobProperties = request.SyncJob.ToDictionary();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloading file {request.FilePath}", DynamicProperties = syncJobProperties }, VerbosityLevel.DEBUG);

            var blobResult = await _blobStorageRepository.DownloadFileAsync(request.FilePath);
            if (blobResult.BlobStatus == BlobStatus.NotFound)
            {
                throw new FileNotFoundException($"File {request.FilePath} was not found");
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloaded file {request.FilePath}", DynamicProperties = syncJobProperties }, VerbosityLevel.DEBUG);
            return (request.FilePath, blobResult.Content.ToString());
        }
    }
}
