// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using System.Collections.Generic;

namespace Hosts.JobTrigger
{
    public class GetJobsFunction
    {
        private readonly IJobTriggerService _jobTriggerService;
        private readonly ILogger<GetJobsFunction> _logger;

        public GetJobsFunction(IJobTriggerService jobTriggerService, ILogger<GetJobsFunction> logger)
        {
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(GetJobsFunction))]
        public async Task<List<SyncJob>> GetJobsToUpdateAsync([ActivityTrigger] object obj)
        {
            _logger.FunctionStarted(nameof(GetJobsFunction));
            var tableQuery = await _jobTriggerService.GetSyncJobsAsync();
            _logger.FunctionCompleted(nameof(GetJobsFunction));
            return tableQuery;
        }
    }
}
