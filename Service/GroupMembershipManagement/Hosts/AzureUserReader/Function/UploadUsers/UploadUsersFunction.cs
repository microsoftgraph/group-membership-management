// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class UploadUsersFunction
    {
        private readonly IAzureUserReaderService _azureUserReaderService = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public UploadUsersFunction(IAzureUserReaderService azureUserReaderService, ILoggingRepository loggingRepository)
        {
            _azureUserReaderService = azureUserReaderService ?? throw new ArgumentNullException(nameof(azureUserReaderService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(UploadUsersFunction))]
        public async Task UploadUsersMemberIdAsync([ActivityTrigger] UploadUsersRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UploadUsersFunction)} function started" });

            var serviceRequest = new UploadRequest
            {
                BlobTargetDirectory = Path.GetDirectoryName(request.AzureUserReaderRequest.BlobPath),
                ContainerName = request.AzureUserReaderRequest.ContainerName,
                Users = request.Users
            };

            await _azureUserReaderService.UploadUsersMemberIdAsync(serviceRequest);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UploadUsersFunction)} function completed" });
        }
    }
}