// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;

namespace Hosts.JobScheduler
{
    public class GetJobsSegmentedFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public GetJobsSegmentedFunction(IJobSchedulingService jobSchedulingService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [FunctionName(nameof(GetJobsSegmentedFunction))]
        public async Task<GetJobsSegmentedResponse> GetJobsToUpdateAsync([ActivityTrigger] GetJobsSegmentedRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var tableQuerySegment = await _jobSchedulingService.GetSyncJobsSegmentAsync(request.PageableQueryResult, request.ContinuationToken);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return new GetJobsSegmentedResponse
            {
                PageableQueryResult = tableQuerySegment.PageableQueryResult,
                JobsSegment = tableQuerySegment.Results,
                ContinuationToken = tableQuerySegment.ContinuationToken
            };
        }
    }
}
