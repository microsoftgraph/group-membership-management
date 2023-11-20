// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.DestinationAttributesUpdater
{
    public class AttributeCacheUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDestinationAttributesUpdaterService _destinationAttributeUpdaterService = null;

        public AttributeCacheUpdaterFunction(ILoggingRepository loggingRepository, IDestinationAttributesUpdaterService destinationAttributeUpdater)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _destinationAttributeUpdaterService = destinationAttributeUpdater ?? throw new ArgumentNullException(nameof(destinationAttributeUpdater));
        }

        [FunctionName(nameof(AttributeCacheUpdaterFunction))]
        public async Task UpdateAttributesAsync([ActivityTrigger] DestinationAttributes destinationAttributes)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(AttributeCacheUpdaterFunction)} function started" }, VerbosityLevel.DEBUG);

            await _destinationAttributeUpdaterService.UpdateAttributes(destinationAttributes);

            var name = string.IsNullOrWhiteSpace(destinationAttributes.Name) ? "N/A" : destinationAttributes.Name;
            var ownersList = destinationAttributes.Owners != null ? string.Join(",", destinationAttributes.Owners) : "N/A";
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(AttributeCacheUpdaterFunction)} function: jobId {destinationAttributes.Id} Name: {name} Owners: ({ownersList})" }, VerbosityLevel.DEBUG);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(AttributeCacheUpdaterFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}
