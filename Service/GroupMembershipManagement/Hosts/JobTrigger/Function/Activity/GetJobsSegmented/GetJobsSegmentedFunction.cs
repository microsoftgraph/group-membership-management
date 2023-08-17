// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;
using System.Collections.Generic;

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
        public async Task<List<SyncJob>> GetJobsToUpdateAsync([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var (tableQuerySegment, proceedJobsFlag) = await _jobTriggerService.GetSyncJobsAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            if (!proceedJobsFlag)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} function is not proceeding with {tableQuerySegment.Count} jobs due to JobTrigger threshold limit exceed." }, VerbosityLevel.DEBUG);
                return new List<SyncJob>();
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsSegmentedFunction)} number of jobs about to be returned: {tableQuerySegment.Count}" }, VerbosityLevel.DEBUG);

            return tableQuerySegment;
        }
    }
}
