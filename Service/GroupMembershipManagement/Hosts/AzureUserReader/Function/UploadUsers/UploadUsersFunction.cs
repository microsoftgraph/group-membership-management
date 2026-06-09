// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Entities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class UploadUsersFunction
    {
        private readonly IAzureUserReaderService _azureUserReaderService;
        private readonly ILogger<UploadUsersFunction> _logger;

        public UploadUsersFunction(IAzureUserReaderService azureUserReaderService, ILogger<UploadUsersFunction> logger)
        {
            _azureUserReaderService = azureUserReaderService ?? throw new ArgumentNullException(nameof(azureUserReaderService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(UploadUsersFunction))]
        public async Task UploadUsersMemberIdAsync([ActivityTrigger] UploadUsersRequest request)
        {
            _logger.FunctionStarted(nameof(UploadUsersFunction));

            var serviceRequest = new UploadRequest
            {
                BlobTargetDirectory = Path.GetDirectoryName(request.AzureUserReaderRequest.BlobPath),
                ContainerName = request.AzureUserReaderRequest.ContainerName,
                Users = request.Users
            };

            await _azureUserReaderService.UploadUsersMemberIdAsync(serviceRequest);

            _logger.FunctionCompleted(nameof(UploadUsersFunction));
        }
    }
}