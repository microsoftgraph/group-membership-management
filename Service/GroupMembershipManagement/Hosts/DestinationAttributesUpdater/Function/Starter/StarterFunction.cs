// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.DestinationAttributesUpdater
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }


        [FunctionName(nameof(StarterFunction))]
        public async Task Run(
            [TimerTrigger("%destinationAttributesUpdaterSchedule%")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);
            await starter.StartNewAsync(nameof(OrchestratorFunction), null);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}
