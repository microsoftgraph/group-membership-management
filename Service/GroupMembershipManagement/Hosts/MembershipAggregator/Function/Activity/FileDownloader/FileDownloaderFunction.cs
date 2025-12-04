// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Helpers;
using Microsoft.Azure.Functions.Worker;
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

        [Function(nameof(FileDownloaderFunction))]
        public async Task<FileDownloaderResponse> DownloadFileAsync([ActivityTrigger] FileDownloaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloading file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var blobResult = await _blobStorageRepository.DownloadFileAsync(request.FilePath);
            if (blobResult.BlobStatus == BlobStatus.NotFound)
            {
                throw new FileNotFoundException($"File {request.FilePath} was not found");
            }

            var content = blobResult.Content;
            var compressedContent = TextCompressor.Compress(content);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloaded file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
            return new FileDownloaderResponse
            {
                FilePath = request.FilePath,
                Content = compressedContent
            };
        }
    }
}
