// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.DestinationAttributesUpdater
{
    public class AttributeCacheUpdaterFunction
    {
        private readonly ILogger<AttributeCacheUpdaterFunction> _logger;
        private readonly IDestinationAttributesUpdaterService _destinationAttributeUpdaterService;

        public AttributeCacheUpdaterFunction(ILogger<AttributeCacheUpdaterFunction> logger, IDestinationAttributesUpdaterService destinationAttributeUpdater)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _destinationAttributeUpdaterService = destinationAttributeUpdater ?? throw new ArgumentNullException(nameof(destinationAttributeUpdater));
        }

        [Function(nameof(AttributeCacheUpdaterFunction))]
        public async Task UpdateAttributesAsync([ActivityTrigger] DestinationAttributes destinationAttributes)
        {
            _logger.FunctionStarted(nameof(AttributeCacheUpdaterFunction));

            await _destinationAttributeUpdaterService.UpdateAttributes(destinationAttributes);

            var name = string.IsNullOrWhiteSpace(destinationAttributes.Name) ? "N/A" : destinationAttributes.Name;
            var ownersList = destinationAttributes.Owners != null ? string.Join(",", destinationAttributes.Owners) : "N/A";
            var email = string.IsNullOrWhiteSpace(destinationAttributes.Email) ? "N/A" : destinationAttributes.Email;

            _logger.AttributesUpdated(destinationAttributes.Id, name, email, ownersList);
            _logger.FunctionCompleted(nameof(AttributeCacheUpdaterFunction));
        }
    }
}
