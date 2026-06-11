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
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
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

        /// <summary>
        /// Download file
        /// </summary>
        /// <param name="request"></param>
        /// <returns>Compressed file content</returns>
        [Function(nameof(FileDownloaderFunction))]
        public async Task<string> DownloadFileAsync([ActivityTrigger] FileDownloaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.DownloadingFile(request.FilePath);

                var blobResult = await _blobStorageRepository.DownloadCacheFileAsync(request.FilePath);
                if (blobResult.BlobStatus == BlobStatus.NotFound)
                {
                    _logger.FileNotFound(request.FilePath);
                    return string.Empty;
                }

                if (request.CheckFileAge && blobResult.LastModified.HasValue && (DateTime.UtcNow - blobResult.LastModified.Value).TotalHours > 167)
                {
                    _logger.FileExpired(request.FilePath);
                    return string.Empty;
                }

                _logger.DownloadedFile(request.FilePath);

                var content = blobResult.Content ?? string.Empty;
                return TextCompressor.Compress(content);
            }
        }
    }
}