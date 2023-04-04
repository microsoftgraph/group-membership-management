// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Hosts.OwnershipReader
{
    public class GetGroupOwnersFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IOwnershipReaderService _ownershipReaderService;

        public GetGroupOwnersFunction(ILoggingRepository loggingRepository, IOwnershipReaderService ownershipReaderService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _ownershipReaderService = ownershipReaderService ?? throw new ArgumentNullException(nameof(ownershipReaderService));
        }

        [FunctionName(nameof(GetGroupOwnersFunction))]
        public async Task<List<Guid>> GetGroupOwnersAsync([ActivityTrigger] GetGroupOwnersRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupOwnersFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var ids = await _ownershipReaderService.GetGroupOwnersAsync(request.GroupId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupOwnersFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return ids;
        }
    }
}
