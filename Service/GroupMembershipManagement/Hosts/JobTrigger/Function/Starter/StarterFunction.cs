// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;

        public StarterFunction(ILogger<StarterFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        [Function(nameof(StarterFunction))]
        public async Task Run(
            [TimerTrigger("%jobTriggerSchedule%")] TimerInfo myTimer,
            [DurableClient] DurableTaskClient starter)
        {
            _logger.FunctionStarted(nameof(StarterFunction));
            await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), null);
            _logger.FunctionCompleted(nameof(StarterFunction));
        }
    }
}

