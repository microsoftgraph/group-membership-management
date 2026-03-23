// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
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
        public async Task<bool> ClaimJobAsync([ActivityTrigger] ClaimJobRequest request)
        {
            if (request.SyncJob is null)
                return false;

            using (_logger.BeginSyncJobScope(request.SyncJob))
            {
                _logger.FunctionStarted(nameof(ClaimJobFunction));
                var claimed = await _jobTriggerService.TryClaimAndUpdateJobAsync(request.Status, request.SyncJob);
                _logger.FunctionCompleted(nameof(ClaimJobFunction));
                return claimed;
            }
        }
    }
}
