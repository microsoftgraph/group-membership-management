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
    public class GetJobsSegmentedFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGroupOwnershipObtainerService _groupOwnershipObtainerService = null;

        public GetJobsSegmentedFunction(ILoggingRepository loggingRepository, IGroupOwnershipObtainerService groupOwnershipObtainerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _groupOwnershipObtainerService = groupOwnershipObtainerService ?? throw new ArgumentNullException(nameof(groupOwnershipObtainerService));
        }

        [FunctionName(nameof(GetJobsSegmentedFunction))]
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
