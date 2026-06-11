// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class FileDownloaderFunction
    {
        private readonly ILogger<FileDownloaderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public FileDownloaderFunction(ILogger<FileDownloaderFunction> logger, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(FileDownloaderFunction))]
        public async Task<FileDownloaderResponse> DownloadFileAsync([ActivityTrigger] FileDownloaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.DownloadingFile(request.FilePath);

                var blobResult = await _blobStorageRepository.DownloadFileAsync(request.FilePath);
                if (blobResult.BlobStatus == BlobStatus.NotFound)
                {
                    throw new FileNotFoundException($"File {request.FilePath} was not found");
                }

                var content = blobResult.Content;
                var compressedContent = TextCompressor.Compress(content);

                _logger.DownloadedFile(request.FilePath);
                return new FileDownloaderResponse
                {
                    FilePath = request.FilePath,
                    Content = compressedContent
                };
            }
        }
    }
}
