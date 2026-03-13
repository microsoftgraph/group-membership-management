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
    public class ParseAndValidateDestinationFunction
    {
        private readonly ILogger<ParseAndValidateDestinationFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public ParseAndValidateDestinationFunction(ILogger<ParseAndValidateDestinationFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(ParseAndValidateDestinationFunction))]
        public async Task<ParsedAndValidateDestinationResponse> ParseAndValidateDestinationAsync([ActivityTrigger] SyncJob syncJob)
        {
            if (syncJob == null)
            {
                return new ParsedAndValidateDestinationResponse
                {
                    IsValid = false,
                    DestinationObject = null
                };
            }

            using var activity = CorrelationActivity.StartSyncJobActivity(nameof(ParseAndValidateDestinationFunction), syncJob);
            using (_logger.BeginSyncJobScope(syncJob))
            {
                _logger.ActivityFunctionStarted(nameof(ParseAndValidateDestinationFunction));
                var parsedAndValidatedDestination = await _jobTriggerService.ParseAndValidateDestinationAsync(syncJob);
                _logger.ActivityFunctionCompleted(nameof(ParseAndValidateDestinationFunction));

                return parsedAndValidatedDestination;
            }
        }
    }
}
