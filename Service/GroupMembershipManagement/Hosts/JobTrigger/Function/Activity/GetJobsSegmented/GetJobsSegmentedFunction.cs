// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;

namespace Hosts.JobTrigger
{
    public class GetJobsSegmentedFunction
    {
        private readonly IJobTriggerService _jobTriggerService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public GetJobsSegmentedFunction(IJobTriggerService jobTriggerService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [FunctionName(nameof(GetJobsSegmentedFunction))]
        public async Task<GetJobsSegmentedResponse> GetJobsToUpdateAsync([ActivityTrigger] GetJobsSegmentedRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var tableQuerySegment = await _jobTriggerService.GetSyncJobsSegmentAsync(request.PageableQueryResult, request.ContinuationToken);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} number of jobs about to be returned: {tableQuerySegment.Results.Count}" }, VerbosityLevel.DEBUG);

            return new GetJobsSegmentedResponse
            {
                PageableQueryResult = tableQuerySegment.PageableQueryResult,
                JobsSegment = tableQuerySegment.Results,
                ContinuationToken = tableQuerySegment.ContinuationToken
            };
        }
    }
}
