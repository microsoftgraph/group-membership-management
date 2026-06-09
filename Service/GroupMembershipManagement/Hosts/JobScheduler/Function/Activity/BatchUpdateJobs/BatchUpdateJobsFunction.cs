// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Services.Contracts;

namespace Hosts.JobScheduler
{
    public class BatchUpdateJobsFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService;
        private readonly ILogger<BatchUpdateJobsFunction> _logger;

        public BatchUpdateJobsFunction(IJobSchedulingService jobSchedulingService, ILogger<BatchUpdateJobsFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [Function(nameof(BatchUpdateJobsFunction))]
        public async Task BatchUpdateJobsAsync([ActivityTrigger] BatchUpdateJobsRequest request)
        {
            _logger.FunctionStarted(nameof(BatchUpdateJobsFunction));
            await _jobSchedulingService.BatchUpdateSyncJobsAsync(request.SyncJobBatch);
            _logger.FunctionCompleted(nameof(BatchUpdateJobsFunction));
        }
    }
}
