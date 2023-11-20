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
    public class DestinationReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDestinationAttributesUpdaterService _destinationAttributeUpdater = null;

        public DestinationReaderFunction(ILoggingRepository loggingRepository, IDestinationAttributesUpdaterService destinationAttributeUpdater)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _destinationAttributeUpdater = destinationAttributeUpdater ?? throw new ArgumentNullException(nameof(destinationAttributeUpdater));
        }

        [FunctionName(nameof(DestinationReaderFunction))]
        public async Task<List<(AzureADGroup Destination, Guid TableId)>> GetDestinationsAsync([ActivityTrigger] string destinationType)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DestinationReaderFunction)} function started"}, VerbosityLevel.DEBUG);

            var destinations = await _destinationAttributeUpdater.GetDestinationsAsync(destinationType);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DestinationReaderFunction)} function completed"}, VerbosityLevel.DEBUG);
            
            return destinations;
        }
    }
}
