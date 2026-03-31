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
    public class AttributeReaderFunction
    {
        private readonly ILogger<AttributeReaderFunction> _logger;
        private readonly IDestinationAttributesUpdaterService _destinationAttributeUpdaterService;

        public AttributeReaderFunction(ILogger<AttributeReaderFunction> logger, IDestinationAttributesUpdaterService destinationAttributeUpdater)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _destinationAttributeUpdaterService = destinationAttributeUpdater ?? throw new ArgumentNullException(nameof(destinationAttributeUpdater));
        }

        [Function(nameof(AttributeReaderFunction))]
        public async Task<List<DestinationAttributes>> GetAttributesAsync([ActivityTrigger] AttributeReaderRequest request)
        {
            _logger.FunctionStarted(nameof(AttributeReaderFunction));

            var destinationAttributes = await _destinationAttributeUpdaterService.GetBulkDestinationAttributesAsync(request.Destinations, request.DestinationType);

            _logger.AttributesRetrieved(destinationAttributes?.Count ?? 0, request.DestinationType);
            _logger.FunctionCompleted(nameof(AttributeReaderFunction));

            return destinationAttributes;
        }
    }
}
