// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using Models;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Microsoft.DurableTask.Client;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.AzureMaintenance
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
            [TimerTrigger("%maintenanceTriggerSchedule%")] TimerInfo myTimer,
            [DurableClient] DurableTaskClient client,
            ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);

            await client.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), null);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}
