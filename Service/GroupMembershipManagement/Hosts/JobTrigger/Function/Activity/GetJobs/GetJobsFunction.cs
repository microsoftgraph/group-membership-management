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
    public class GetJobsFunction
    {
        private readonly IJobTriggerService _jobTriggerService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public GetJobsFunction(IJobTriggerService jobTriggerService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [FunctionName(nameof(GetJobsFunction))]
        public async Task<List<SyncJob>> GetJobsToUpdateAsync([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var (tableQuery, jobTriggerThresholdExceeded) = await _jobTriggerService.GetSyncJobsAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            if (jobTriggerThresholdExceeded)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsFunction)} function is not proceeding with {tableQuery.Count} jobs due to JobTrigger threshold limit exceed." }, VerbosityLevel.DEBUG);
                return new List<SyncJob>();
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsFunction)} number of jobs about to be returned: {tableQuery.Count}" }, VerbosityLevel.DEBUG);

            return tableQuery;
        }
    }
}
