// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Services;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class ParseAndValidateDestinationFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public ParseAndValidateDestinationFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService)); ;
        }

        [FunctionName(nameof(ParseAndValidateDestinationFunction))]
        public async Task<(bool IsValid, string DestinationObject)> ParseAndValidateDestinationAsync([ActivityTrigger] SyncJob syncJob)
        {

            if (syncJob == null) return (false, null);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ParseAndValidateDestinationFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            _jobTriggerService.RunId = syncJob.RunId ?? Guid.Empty;
            var parsedAndValidatedDestination = await _jobTriggerService.ParseAndValidateDestinationAsync(syncJob);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ParseAndValidateDestinationFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            return parsedAndValidatedDestination;
        }
    }
}