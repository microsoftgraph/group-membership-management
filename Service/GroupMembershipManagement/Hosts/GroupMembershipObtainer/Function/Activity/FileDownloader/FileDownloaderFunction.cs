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

            var content = blobResult.Content ?? string.Empty;
            return TextCompressor.Compress(content);
        }
    }
}
