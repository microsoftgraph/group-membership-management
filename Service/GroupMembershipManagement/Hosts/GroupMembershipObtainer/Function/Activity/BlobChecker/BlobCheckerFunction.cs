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
    public class BlobCheckerFunction
    {
        private readonly ILogger<BlobCheckerFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public BlobCheckerFunction(ILogger<BlobCheckerFunction> logger, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(BlobCheckerFunction))]
        public async Task<BlobResult> CheckBlobAsync([ActivityTrigger] BlobCheckerRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(BlobCheckerFunction));
                var result = await _blobStorageRepository.FindLatestFileAsync(request.Prefix);
                _logger.FunctionCompleted(nameof(BlobCheckerFunction));
                return result;
            }
        }
    }
}