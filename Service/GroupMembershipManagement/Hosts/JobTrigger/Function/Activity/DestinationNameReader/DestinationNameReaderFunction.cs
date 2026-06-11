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
    public class DestinationNameReaderFunction
    {
        private readonly ILogger<DestinationNameReaderFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public DestinationNameReaderFunction(ILogger<DestinationNameReaderFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(DestinationNameReaderFunction))]
        public async Task<string> GetDestinationNameAsync([ActivityTrigger] SyncJob syncJob)
        {
            if (syncJob == null)
                return null;

            using (_logger.BeginSyncJobScope(syncJob))
            {
                _logger.FunctionStarted(nameof(DestinationNameReaderFunction));
                var destinationName = await _jobTriggerService.GetDestinationNameAsync(syncJob);
                _logger.FunctionCompleted(nameof(DestinationNameReaderFunction));

                return destinationName;
            }
        }
    }
}
