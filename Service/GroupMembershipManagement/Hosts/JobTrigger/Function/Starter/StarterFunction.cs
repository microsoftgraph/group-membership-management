// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Services.Contracts;
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

        [FunctionName(nameof(StarterFunction))]
        public async Task Run(
            [TimerTrigger("%jobTriggerSchedule%")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" });
            await starter.StartNewAsync(nameof(OrchestratorFunction), null);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" });
        }
    }
}
