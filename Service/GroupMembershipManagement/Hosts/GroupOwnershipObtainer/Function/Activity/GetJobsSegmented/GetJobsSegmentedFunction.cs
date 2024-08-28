// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.GroupOwnershipObtainer
{
    public class GetJobsSegmentedFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGroupOwnershipObtainerService _groupOwnershipObtainerService = null;

        public GetJobsSegmentedFunction(ILoggingRepository loggingRepository, IGroupOwnershipObtainerService groupOwnershipObtainerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _groupOwnershipObtainerService = groupOwnershipObtainerService ?? throw new ArgumentNullException(nameof(groupOwnershipObtainerService));
        }

        [Function(nameof(GetJobsSegmentedFunction))]
        public async Task<List<SyncJob>> GetJobsAsync([ActivityTrigger] GetJobsSegmentedRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function started at: {DateTime.UtcNow}", RunId = request.RunId }, VerbosityLevel.DEBUG);
            var responsePage = await _groupOwnershipObtainerService.GetSyncJobsSegmentAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function completed at: {DateTime.UtcNow}", RunId = request.RunId }, VerbosityLevel.DEBUG);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} number of jobs about to be returned: {responsePage.Count}", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return responsePage;
        }
    }
}
