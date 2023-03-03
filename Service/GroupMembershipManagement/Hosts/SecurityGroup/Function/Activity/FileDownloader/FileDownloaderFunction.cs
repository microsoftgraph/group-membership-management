// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.Helpers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
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

        /// <summary>
        /// Download file
        /// </summary>
        /// <param name="request"></param>
        /// <returns>Compressed file content</returns>
        [FunctionName(nameof(FileDownloaderFunction))]
        public async Task<string> DownloadFileAsync([ActivityTrigger] FileDownloaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloading file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var blobResult = await _blobStorageRepository.DownloadCacheFileAsync(request.FilePath);
            if (blobResult.BlobStatus == BlobStatus.NotFound)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"File {request.FilePath} does not exist", RunId = request.SyncJob.RunId }, VerbosityLevel.INFO);
                return string.Empty;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloaded file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var content = blobResult.Content.ToString() ?? string.Empty;
            return TextCompressor.Compress(content);
        }
    }
}
