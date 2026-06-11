// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class FileDeleterFunction
    {
        private readonly ILogger<FileDeleterFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public FileDeleterFunction(ILogger<FileDeleterFunction> logger, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(FileDeleterFunction))]
        public async Task DeleteFileAsync([ActivityTrigger] FileDeleterRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.DeletingFile(request.FilePath);
                await _blobStorageRepository.DeleteFilesByPrefixAsync(request.FilePath, true);
                _logger.DeletedFile(request.FilePath);
            }
        }
    }
}