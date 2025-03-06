// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.DestinationAttributesUpdater
{
    public class AttributeReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDestinationAttributesUpdaterService _destinationAttributeUpdaterService = null;

        public AttributeReaderFunction(ILoggingRepository loggingRepository, IDestinationAttributesUpdaterService destinationAttributeUpdater)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _destinationAttributeUpdaterService = destinationAttributeUpdater ?? throw new ArgumentNullException(nameof(destinationAttributeUpdater));
        }

        [FunctionName(nameof(AttributeReaderFunction))]
        public async Task<List<DestinationAttributes>> GetAttributesAsync([ActivityTrigger] AttributeReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(AttributeReaderFunction)} function started" }, VerbosityLevel.DEBUG);

            var destinationAttributes = await _destinationAttributeUpdaterService.GetBulkDestinationAttributesAsync(request.Destinations, request.DestinationType);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(AttributeReaderFunction)} function completed" }, VerbosityLevel.DEBUG);

            return destinationAttributes;
        }
    }
}
