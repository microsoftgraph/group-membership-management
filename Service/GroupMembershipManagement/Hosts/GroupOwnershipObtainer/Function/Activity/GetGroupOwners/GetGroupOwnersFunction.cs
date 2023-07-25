// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class GetGroupOwnersFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGroupOwnershipObtainerService _groupOwnershipObtainerService;

        public GetGroupOwnersFunction(ILoggingRepository loggingRepository, IGroupOwnershipObtainerService groupOwnershipObtainerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _groupOwnershipObtainerService = groupOwnershipObtainerService ?? throw new ArgumentNullException(nameof(groupOwnershipObtainerService));
        }

        [FunctionName(nameof(GetGroupOwnersFunction))]
        public async Task<List<Guid>> GetGroupOwnersAsync([ActivityTrigger] GetGroupOwnersRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupOwnersFunction)} function started at: {DateTime.UtcNow}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
            var ids = await _groupOwnershipObtainerService.GetGroupOwnersAsync(request.GroupId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetGroupOwnersFunction)} function completed at: {DateTime.UtcNow}", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            return ids;
        }
    }
}
