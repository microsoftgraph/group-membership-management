// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }


        [Function(nameof(StarterFunction))]
        public async Task Run(
            [TimerTrigger("%jobTriggerSchedule%")] TimerInfo myTimer,
            [DurableClient] DurableTaskClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);
            await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), null);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}

