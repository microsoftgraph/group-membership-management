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
    public class DeleteBlobFunction
    {
        private readonly ILogger<DeleteBlobFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public DeleteBlobFunction(ILogger<DeleteBlobFunction> logger, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(DeleteBlobFunction))]
        public async Task DeleteAsync([ActivityTrigger] DeleteBlobRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(DeleteBlobFunction));
                string prefix = $"{request.GroupId}/userUploads/{request.SyncJob.RunId.GetValueOrDefault()}_GroupMembership_{request.CurrentPart}_";
                await _blobStorageRepository.DeleteBlobsAsync(prefix);
                _logger.FunctionCompleted(nameof(DeleteBlobFunction));
            }
        }
    }
}