// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.DestinationAttributesUpdater
{
    public class DestinationReaderFunction
    {
        private readonly ILogger<DestinationReaderFunction> _logger;
        private readonly IDestinationAttributesUpdaterService _destinationAttributeUpdater;

        public DestinationReaderFunction(ILogger<DestinationReaderFunction> logger, IDestinationAttributesUpdaterService destinationAttributeUpdater)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _destinationAttributeUpdater = destinationAttributeUpdater ?? throw new ArgumentNullException(nameof(destinationAttributeUpdater));
        }

        [Function(nameof(DestinationReaderFunction))]
        public async Task<List<DestinationInfo>> GetDestinationsAsync([ActivityTrigger] string destinationType)
        {
            _logger.FunctionStarted(nameof(DestinationReaderFunction));

            var destinations = await _destinationAttributeUpdater.GetDestinationsAsync(destinationType);

            _logger.DestinationsRetrieved(destinations?.Count ?? 0, destinationType);
            _logger.FunctionCompleted(nameof(DestinationReaderFunction));

            return destinations;
        }
    }
}
