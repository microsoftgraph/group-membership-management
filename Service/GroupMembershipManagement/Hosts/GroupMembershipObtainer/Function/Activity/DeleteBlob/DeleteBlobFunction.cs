// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
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

        [Function(nameof(DeleteBlobFunction))]
        public async Task DeleteAsync([ActivityTrigger] DeleteBlobRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeleteBlobFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            string prefix = $"{request.GroupId}/userUploads/{request.RunId}_GroupMembership_{request.CurrentPart}_";
            await _blobStorageRepository.DeleteBlobsAsync(prefix);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DeleteBlobFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}