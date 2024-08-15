// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Functions.Worker;

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

        [Function(nameof(GetJobsFunction))]
        public async Task<List<SyncJob>> GetJobsToUpdateAsync([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var tableQuery = await _jobTriggerService.GetSyncJobsAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            return tableQuery;
        }
    }
}
