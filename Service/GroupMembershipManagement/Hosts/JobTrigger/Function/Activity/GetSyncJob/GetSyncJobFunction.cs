// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class GetSyncJobFunction
    {
        private readonly ILogger<GetSyncJobFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public GetSyncJobFunction(ILogger<GetSyncJobFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(GetSyncJobFunction))]
        public async Task<SyncJob> GetSyncJobByIdAsync([ActivityTrigger] Guid syncJobId)
        {
            _logger.FunctionStarted(nameof(GetSyncJobFunction));
            var syncJob = await _jobTriggerService.GetSyncJobByIdAsync(syncJobId);
            _logger.FunctionCompleted(nameof(GetSyncJobFunction));
            return syncJob;
        }
    }
}
