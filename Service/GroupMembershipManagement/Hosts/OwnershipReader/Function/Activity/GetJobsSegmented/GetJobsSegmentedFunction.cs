// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;

namespace Hosts.OwnershipReader
{
    public class GetJobsSegmentedFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IOwnershipReaderService _ownershipReaderService = null;

        public GetJobsSegmentedFunction(ILoggingRepository loggingRepository, IOwnershipReaderService ownershipReaderService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _ownershipReaderService = ownershipReaderService ?? throw new ArgumentNullException(nameof(ownershipReaderService));
        }

        [FunctionName(nameof(GetJobsSegmentedFunction))]
        public async Task<GetJobsSegmentedResponse> GetJobsAsync([ActivityTrigger] GetJobsSegmentedRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var tableQuerySegment = await _ownershipReaderService.GetSyncJobsSegmentAsync(request.PageableQueryResult, request.ContinuationToken);
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
