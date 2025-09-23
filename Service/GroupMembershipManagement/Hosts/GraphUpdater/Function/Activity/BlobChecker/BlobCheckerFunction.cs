// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class BlobCheckerFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public BlobCheckerFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [FunctionName(nameof(BlobCheckerFunction))]
        public async Task<BlobResult> CheckBlobAsync([ActivityTrigger] BlobCheckerRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BlobCheckerFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            var result = await _blobStorageRepository.FindLatestFileAsync(request.Prefix);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BlobCheckerFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return result;
        }
    }
}