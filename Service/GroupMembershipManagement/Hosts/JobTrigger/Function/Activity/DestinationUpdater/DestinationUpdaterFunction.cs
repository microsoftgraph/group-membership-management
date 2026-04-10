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
    public class DestinationUpdaterFunction
    {
        private readonly ILogger<DestinationUpdaterFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public DestinationUpdaterFunction(ILogger<DestinationUpdaterFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(DestinationUpdaterFunction))]
        public async Task UpdateDestinationAsync([ActivityTrigger] DestinationUpdaterRequest request)
        {
            _logger.FunctionStarted(nameof(DestinationUpdaterFunction));
            await _jobTriggerService.UpdateSyncJobDestinationAsync(request.JobId, request.Destination);
            _logger.FunctionCompleted(nameof(DestinationUpdaterFunction));
        }
    }
}
