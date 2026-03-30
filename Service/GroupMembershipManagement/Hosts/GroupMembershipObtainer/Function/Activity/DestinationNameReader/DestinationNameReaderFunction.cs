// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class DestinationNameReaderFunction
    {
        private readonly ILogger<DestinationNameReaderFunction> _logger;
        private readonly SGMembershipCalculator _calculator = null;

        public DestinationNameReaderFunction(ILogger<DestinationNameReaderFunction> logger, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(DestinationNameReaderFunction))]
        public async Task<string> GetDestinationNameAsync([ActivityTrigger] DestinationNameReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(DestinationNameReaderFunction));

                if (request.SyncJob == null)
                    return null;

                var destinationName = await _calculator.GetDestinationNameAsync(request.SyncJob);
                _logger.FunctionCompleted(nameof(DestinationNameReaderFunction));

                return destinationName;
            }
        }
    }
}