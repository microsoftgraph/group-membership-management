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
    public class ClaimJobFunction
    {
        private readonly ILogger<ClaimJobFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public ClaimJobFunction(ILogger<ClaimJobFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(ClaimJobFunction))]
        public async Task<SyncJob?> ClaimJobAsync([ActivityTrigger] ClaimJobRequest request)
        {
            if (request.SyncJob is null)
                return null;

            using (_logger.BeginSyncJobScope(request.SyncJob))
            {
                _logger.FunctionStarted(nameof(ClaimJobFunction));
                var claimedJob = await _jobTriggerService.TryClaimAndUpdateJobAsync(request.Status, request.SyncJob);
                _logger.FunctionCompleted(nameof(ClaimJobFunction));
                return claimedJob;
            }
        }
    }
}
