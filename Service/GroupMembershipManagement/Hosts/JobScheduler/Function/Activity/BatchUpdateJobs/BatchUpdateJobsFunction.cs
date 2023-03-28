// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;

namespace Hosts.JobScheduler
{
    public class BatchUpdateJobsFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public BatchUpdateJobsFunction(IJobSchedulingService jobSchedulingService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [FunctionName(nameof(BatchUpdateJobsFunction))]
        public async Task BatchUpdateJobsAsync([ActivityTrigger] BatchUpdateJobsRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BatchUpdateJobsFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            await _jobSchedulingService.BatchUpdateSyncJobsAsync(request.SyncJobBatch);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BatchUpdateJobsFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
        }
    }
}
