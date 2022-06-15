// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class FileDownloaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository = null;

        public FileDownloaderFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [FunctionName(nameof(FileDownloaderFunction))]
        public async Task<string> DownloadFileAsync([ActivityTrigger] FileDownloaderRequest request)
        {
            _loggingRepository.SyncJobProperties = request.SyncJob?.ToDictionary();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloading file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var blobResult = await _blobStorageRepository.DownloadFileAsync(request.FilePath);
            if (blobResult.BlobStatus == BlobStatus.NotFound)
            {
                throw new FileNotFoundException($"File {request.FilePath} was not found");
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Downloaded file {request.FilePath}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var content = blobResult.Content.ToString();
            return content;
        }
    }
}
