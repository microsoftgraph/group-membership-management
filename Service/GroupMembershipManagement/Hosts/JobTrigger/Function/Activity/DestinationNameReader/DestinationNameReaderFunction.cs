// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.JobTrigger
{
    public class DestinationNameReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public DestinationNameReaderFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService)); ;
        }

        [Function(nameof(DestinationNameReaderFunction))]
        public async Task<string> GetDestinationNameAsync([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DestinationNameReaderFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            if (syncJob == null)
                return null;
            
            _jobTriggerService.RunId = syncJob.RunId ?? Guid.Empty;
            var destinationName = await _jobTriggerService.GetDestinationNameAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DestinationNameReaderFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            return destinationName;
        }
    }
}