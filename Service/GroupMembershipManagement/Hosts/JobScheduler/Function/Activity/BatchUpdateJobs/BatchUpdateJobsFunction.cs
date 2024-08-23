// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

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

        [Function(nameof(BatchUpdateJobsFunction))]
        public async Task BatchUpdateJobsAsync([ActivityTrigger] BatchUpdateJobsRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BatchUpdateJobsFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            await _jobSchedulingService.BatchUpdateSyncJobsAsync(request.SyncJobBatch);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BatchUpdateJobsFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
        }
    }
}
