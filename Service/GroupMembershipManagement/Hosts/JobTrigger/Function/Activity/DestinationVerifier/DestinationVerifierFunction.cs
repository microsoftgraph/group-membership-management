// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class DestinationVerifierFunction
    {
        private readonly ILogger<DestinationVerifierFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public DestinationVerifierFunction(ILogger<DestinationVerifierFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(DestinationVerifierFunction))]
        public async Task<DestinationVerifierResult> VerifyDestinationAsync([ActivityTrigger] SyncJob syncJob)
        {
            var verifierResult = DestinationVerifierResult.NotFound;

            if (syncJob != null)
            {
                using (_logger.BeginSyncJobScope(syncJob))
                {
                    _logger.ActivityFunctionStarted(nameof(DestinationVerifierFunction));
                    verifierResult = await _jobTriggerService.DestinationExistsAndGMMCanWriteToItAsync(syncJob);

                    if (verifierResult == DestinationVerifierResult.Success)
                    {
                        var endpoints = await _jobTriggerService.GetGroupEndpointsAsync(syncJob);
                        _logger.LinkedServices(string.Join(",", endpoints));
                    }

                    _logger.ActivityFunctionCompleted(nameof(DestinationVerifierFunction));
                }
            }
            return verifierResult;
        }
    }
}
