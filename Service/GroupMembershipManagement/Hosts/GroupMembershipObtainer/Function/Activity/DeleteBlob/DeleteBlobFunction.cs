// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class DeleteBlobFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public DeleteBlobFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [FunctionName(nameof(DeleteBlobFunction))]
        public async Task DeleteAsync([ActivityTrigger] DeleteBlobRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting userUploads in {request.GroupId}", RunId = request.RunId }, VerbosityLevel.DEBUG);
            string prefix = $"{request.GroupId}/userUploads/{request.RunId}_GroupMembership_{request.CurrentPart}";
            await _blobStorageRepository.DeleteBlobsAsync(prefix);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Deleting userUploads in {request.GroupId}", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}