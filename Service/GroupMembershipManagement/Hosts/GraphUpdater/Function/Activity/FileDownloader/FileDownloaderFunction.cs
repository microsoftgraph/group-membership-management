// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class FileDownloaderFunction
    {
        private readonly ILogger<FileDownloaderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository = null;

        public FileDownloaderFunction(ILogger<FileDownloaderFunction> logger, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(FileDownloaderFunction))]
        public async Task<string> DownloadFileAsync([ActivityTrigger] FileDownloaderRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            var blobResult = new BlobResult { BlobStatus = BlobStatus.NotFound };

            _logger.DownloadingFile(request.FilePath);

            if (request.FilePath.Contains("cache"))
            {
                blobResult = await _blobStorageRepository.DownloadCacheFileAsync(request.FilePath);
                if (blobResult.BlobStatus == BlobStatus.NotFound)
                {
                    _logger.CacheFileNotFound(request.FilePath);
                    return string.Empty;
                }
            }
            else
            {
                blobResult = await _blobStorageRepository.DownloadFileAsync(request.FilePath);
                if (blobResult.BlobStatus == BlobStatus.NotFound)
                {
                    throw new FileNotFoundException($"File {request.FilePath} was not found");
                }
            }

            _logger.DownloadedFile(request.FilePath);

            var content = blobResult.Content;
            return content;
        }
    }
}
