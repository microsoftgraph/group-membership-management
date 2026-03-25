// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models.Helpers;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class FileUploaderFunction
    {
        private readonly ILogger<FileUploaderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public FileUploaderFunction(ILogger<FileUploaderFunction> logger, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(FileUploaderFunction))]
        public async Task UploadFileAsync([ActivityTrigger] FileUploaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.UploadingFile(request.FilePath);
                await _blobStorageRepository.UploadFileAsync(request.FilePath, TextCompressor.Decompress(request.Content));
                _logger.UploadedFile(request.FilePath);
            }
        }
    }
}
